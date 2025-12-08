using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class ParenthesizedExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.ParenthesizedExpression, Transform);
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax node)
    {
        var parenthesized = (ParenthesizedExpressionSyntax)node;
        // AST doesn't have ParenthesizedExpression, just return the inner expression.
        return context.BuildExpression(parenthesized.Expression);
    }
}
