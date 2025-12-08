using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class CastExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.CastExpression, Transform);
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax node)
    {
        var cast = (CastExpressionSyntax)node;
        // Casts are mostly compile-time in Luau (types are erased).
        // Unless it's a numeric cast that changes representation?
        // For now, just return the inner expression.
        return context.BuildExpression(cast.Expression);
    }
}
