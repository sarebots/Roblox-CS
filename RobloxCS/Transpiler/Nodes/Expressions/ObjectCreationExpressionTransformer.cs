using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Expressions;
using RobloxCS.TranspilerV2.Builders;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class ObjectCreationExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.ObjectCreationExpression, TransformObjectCreation);
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.ImplicitObjectCreationExpression, TransformImplicitObjectCreation);
    }

    private static Expression TransformObjectCreation(TransformContext context, ExpressionSyntax syntax)
    {
        var objectCreation = (ObjectCreationExpressionSyntax)syntax;
        return ExpressionBuilder.HandleObjectCreationExpressionLegacy(objectCreation, context.TranspilationContext);
    }

    private static Expression TransformImplicitObjectCreation(TransformContext context, ExpressionSyntax syntax)
    {
        var implicitCreation = (ImplicitObjectCreationExpressionSyntax)syntax;
        return ExpressionBuilder.HandleImplicitObjectCreationExpressionLegacy(implicitCreation, context.TranspilationContext);
    }
}
