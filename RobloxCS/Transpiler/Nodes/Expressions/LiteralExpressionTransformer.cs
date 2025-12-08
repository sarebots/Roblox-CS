using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Expressions;
using RobloxCS.TranspilerV2.Builders;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class LiteralExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.NumericLiteralExpression, Transform);
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.StringLiteralExpression, Transform);
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.TrueLiteralExpression, Transform);
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.FalseLiteralExpression, Transform);
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.NullLiteralExpression, Transform);
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax syntax)
    {
        var literal = (LiteralExpressionSyntax)syntax;
        return ExpressionBuilder.HandleLiteralExpressionLegacy(literal, context.TranspilationContext);
    }
}
