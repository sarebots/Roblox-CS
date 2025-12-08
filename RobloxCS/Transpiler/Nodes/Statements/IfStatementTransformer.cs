using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Statements;
using RobloxCS.TranspilerV2.Builders;
using RobloxCS.TranspilerV2.Nodes.Common;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class IfStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.IfStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax syntax)
    {
        var ifStatement = (IfStatementSyntax)syntax;
        var condition = ExpressionBuilder.BuildFromSyntax(ifStatement.Condition, context);
        var thenBlock = StatementTransformerUtilities.BuildBlockFromStatement(ifStatement.Statement, context);

        Block? elseBlock = null;
        if (ifStatement.Else is { Statement: { } elseSyntax })
        {
            elseBlock = StatementTransformerUtilities.BuildBlockFromStatement(elseSyntax, context);
        }

        return new If
        {
            Condition = condition,
            ThenBody = thenBlock,
            ElseBody = elseBlock,
        };
    }
}
