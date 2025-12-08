using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Expressions;
using RobloxCS.TranspilerV2.Builders;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class CollectionExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.CollectionExpression, Transform);
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax syntax)
    {
        var collection = (CollectionExpressionSyntax)syntax;
        return ExpressionBuilder.HandleCollectionExpressionLegacy(collection, context.TranspilationContext);
    }
}
