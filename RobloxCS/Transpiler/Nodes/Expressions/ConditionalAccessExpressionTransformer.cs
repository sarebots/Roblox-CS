using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Expressions;
using RobloxCS.TranspilerV2.Builders;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class ConditionalAccessExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.ConditionalAccessExpression, Transform);
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax syntax)
    {
        var conditionalAccess = (ConditionalAccessExpressionSyntax)syntax;
        return ExpressionBuilder.HandleConditionalAccessExpressionLegacy(conditionalAccess, context.TranspilationContext);
    }
}
