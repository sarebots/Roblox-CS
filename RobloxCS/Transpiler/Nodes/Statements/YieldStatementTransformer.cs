using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Functions;
using RobloxCS.AST.Statements;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2.Builders;
using RobloxCS.TranspilerV2.Nodes.Common;

using FunctionCallAst = RobloxCS.AST.Expressions.FunctionCall;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class YieldStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.YieldReturnStatement, Transform);
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.YieldBreakStatement, Transform);
    }

    public static Statement Transform(TranspilationContext context, StatementSyntax syntax)
    {
        if (syntax is not YieldStatementSyntax node)
        {
            throw Logger.UnsupportedError(syntax, "Expected yield statement syntax.");
        }

        if (!context.TryGetGeneratorScope(out var scope))
        {
            throw Logger.UnsupportedError(node, "yield statements outside iterator methods are not supported.");
        }

        var block = Block.Empty();
        AppendPrerequisites(context, block);

        if (node.ReturnOrBreakKeyword.IsKind(SyntaxKind.ReturnKeyword))
        {
            if (node.Expression is null)
            {
                throw Logger.UnsupportedError(node, "yield return without a value");
            }

            var expression = ExpressionBuilder.BuildFromSyntax(node.Expression, context);
            AppendPrerequisites(context, block);

            var generatorCall = FunctionCallAst.Basic("CS.Generator.yieldValue", scope.StateExpression, expression);
            block.AddStatement(new FunctionCallStatement
            {
                Prefix = generatorCall.Prefix,
                Suffixes = generatorCall.Suffixes,
            });

            block.AddStatement(Return.FromExpressions([expression]));
            return DoStatement.FromBlock(block);
        }

        var closeCall = FunctionCallAst.Basic("CS.Generator.close", scope.StateExpression);
        block.AddStatement(new FunctionCallStatement
        {
            Prefix = closeCall.Prefix,
            Suffixes = closeCall.Suffixes,
        });

        if (scope.HasBreakHandler)
        {
            var breakCall = FunctionCallAst.Basic("_breakIteration");
            block.AddStatement(new FunctionCallStatement
            {
                Prefix = breakCall.Prefix,
                Suffixes = breakCall.Suffixes,
            });
        }

        block.AddStatement(Return.Empty());
        return DoStatement.FromBlock(block);
    }

    private static void AppendPrerequisites(TranspilationContext context, Block block)
    {
        foreach (var prerequisite in context.ConsumePrerequisites())
        {
            block.AddStatement(prerequisite);
        }
    }
}
