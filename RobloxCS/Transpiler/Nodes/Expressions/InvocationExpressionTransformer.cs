using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Expressions;
using RobloxCS.TranspilerV2.Builders;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class InvocationExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.InvocationExpression, Transform);
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax syntax)
    {
        var invocation = (InvocationExpressionSyntax)syntax;
        if (ExpressionBuilder.TryBuildInvocationMacroExpression(invocation, context.TranspilationContext, out var macroExpression))
        {
            return macroExpression;
        }
        return ExpressionBuilder.BuildFunctionCall(invocation, context.TranspilationContext);
    }
}
