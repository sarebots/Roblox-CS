using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Expressions;
using RobloxCS.TranspilerV2.Builders;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class ArrayCreationExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.ArrayCreationExpression, TransformArrayCreation);
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.ImplicitArrayCreationExpression, TransformImplicitArrayCreation);
    }

    private static Expression TransformArrayCreation(TransformContext context, ExpressionSyntax syntax)
    {
        var arrayCreation = (ArrayCreationExpressionSyntax)syntax;
        return ExpressionBuilder.HandleArrayCreationExpressionLegacy(arrayCreation, context.TranspilationContext);
    }

    private static Expression TransformImplicitArrayCreation(TransformContext context, ExpressionSyntax syntax)
    {
        var implicitArray = (ImplicitArrayCreationExpressionSyntax)syntax;
        return ExpressionBuilder.HandleImplicitArrayCreationExpressionLegacy(implicitArray, context.TranspilationContext);
    }
}
