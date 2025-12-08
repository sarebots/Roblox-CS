using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.Shared;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

using FunctionCallAst = RobloxCS.AST.Expressions.FunctionCall;

internal static class UnaryExpressionTransformer
{
    private static readonly SyntaxKind[] SupportedKinds =
    {
        SyntaxKind.UnaryMinusExpression,
        SyntaxKind.UnaryPlusExpression,
        SyntaxKind.LogicalNotExpression,
        SyntaxKind.BitwiseNotExpression,
    };

    public static void Register()
    {
        foreach (var kind in SupportedKinds)
        {
            TransformerRegistry.RegisterExpressionTransformer(kind, Transform);
        }
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax syntax)
    {
        if (syntax is not PrefixUnaryExpressionSyntax unary)
        {
            return context.BuildExpressionWithoutTransformers(syntax);
        }

        if (unary.OperatorToken.Text == "^")
        {
            Logger.UnsupportedError(unary, "'^' unary operator", true);
            return context.BuildExpressionWithoutTransformers(syntax);
        }

        if (unary.OperatorToken.IsKind(SyntaxKind.PlusToken))
        {
            return context.BuildExpression(unary.Operand);
        }

        var operand = context.BuildExpression(unary.Operand);
        var mappedOperator = StandardUtility.GetMappedOperator(unary.OperatorToken.Text);
        var bit32Method = StandardUtility.GetBit32MethodName(mappedOperator);
        if (bit32Method is not null)
        {
            return FunctionCallAst.Basic($"bit32.{bit32Method}", operand);
        }

        var op = unary.OperatorToken.Kind() switch
        {
            SyntaxKind.MinusToken => UnOp.Minus,
            SyntaxKind.ExclamationToken => UnOp.Not,
            SyntaxKind.TildeToken => UnOp.BitwiseNot,
            _ => throw new NotSupportedException($"Unsupported unary operator token '{unary.OperatorToken.Kind()}'"),
        };

        return new UnaryOperatorExpression
        {
            Op = op,
            Operand = operand,
        };
    }
}
