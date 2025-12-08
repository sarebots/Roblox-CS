using System;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Statements;
using RobloxCS.TranspilerV2;
using RobloxCS.TranspilerV2.Builders;
using RobloxCS.TranspilerV2.Nodes.Common;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class ForStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.ForStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax syntax)
    {
        var forStatement = (ForStatementSyntax)syntax;

        if (!TryBuildNumericFor(forStatement, context, out var statement))
        {
            throw new NotSupportedException("Only simple numeric for loops are supported at this time.");
        }

        return statement;
    }

    private static bool TryBuildNumericFor(ForStatementSyntax forStmt, TranspilationContext ctx, out Statement statement)
    {
        statement = null!;

        if (!IsSimpleNumericFor(forStmt, ctx, out var identifier, out var startExpr, out var limitExpr, out var stepExpr))
        {
            return false;
        }

        ctx.PushScope();
        ctx.LoopStack.Push(new LoopInfo());
        var bodyBlock = StatementTransformerUtilities.BuildBlockFromStatement(forStmt.Statement, ctx);
        ctx.LoopStack.Pop();
        ctx.PopScope();

        statement = new NumericFor
        {
            Name = VarName.FromString(identifier),
            Start = startExpr,
            End = limitExpr,
            Step = stepExpr,
            Body = bodyBlock,
        };

        return true;
    }

    private static bool IsSimpleNumericFor(
        ForStatementSyntax forStmt,
        TranspilationContext ctx,
        out string identifier,
        out Expression startExpr,
        out Expression limitExpr,
        out Expression stepExpr)
    {
        identifier = string.Empty;
        startExpr = limitExpr = stepExpr = SymbolExpression.FromString("0");

        if (forStmt.Declaration is null || forStmt.Declaration.Variables.Count != 1)
        {
            return false;
        }

        if (forStmt.Initializers.Count > 0 || forStmt.Condition is null || forStmt.Incrementors.Count != 1)
        {
            return false;
        }

        var variable = forStmt.Declaration.Variables[0];
        if (variable.Initializer?.Value is not ExpressionSyntax initValue)
        {
            return false;
        }

        identifier = variable.Identifier.ValueText;
        startExpr = ExpressionBuilder.BuildFromSyntax(initValue, ctx);

        if (forStmt.Condition is not BinaryExpressionSyntax condition
            || condition.Kind() is not (SyntaxKind.LessThanOrEqualExpression or SyntaxKind.LessThanExpression)
            || condition.Left is not IdentifierNameSyntax conditionIdentifier
            || conditionIdentifier.Identifier.ValueText != identifier)
        {
            return false;
        }

        limitExpr = ExpressionBuilder.BuildFromSyntax(condition.Right, ctx);

        if (condition.Kind() == SyntaxKind.LessThanExpression)
        {
            limitExpr = new BinaryOperatorExpression
            {
                Left = limitExpr,
                Right = NumberExpression.From(1),
                Op = BinOp.Minus,
            };
        }

        if (forStmt.Incrementors[0] is PostfixUnaryExpressionSyntax incrementUnary)
        {
            if (incrementUnary.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PreIncrementExpression
                && incrementUnary.Operand is IdentifierNameSyntax { Identifier.ValueText: var incName } && incName == identifier)
            {
                stepExpr = NumberExpression.From(1);
                return true;
            }
        }

        if (forStmt.Incrementors[0] is PrefixUnaryExpressionSyntax prefixUnary)
        {
            if (prefixUnary.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PostIncrementExpression
                && prefixUnary.Operand is IdentifierNameSyntax { Identifier.ValueText: var incName2 } && incName2 == identifier)
            {
                stepExpr = NumberExpression.From(1);
                return true;
            }
        }

        if (forStmt.Incrementors[0] is AssignmentExpressionSyntax incrementAssign
            && incrementAssign.Kind() == SyntaxKind.AddAssignmentExpression
            && incrementAssign.Left is IdentifierNameSyntax { Identifier.ValueText: var varName } && varName == identifier)
        {
            stepExpr = ExpressionBuilder.BuildFromSyntax(incrementAssign.Right, ctx);
            return true;
        }

        return false;
    }
}
