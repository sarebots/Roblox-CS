using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Expressions;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

using FunctionCallAst = RobloxCS.AST.Expressions.FunctionCall;

internal static class AwaitExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.AwaitExpression, Transform);
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax syntax)
    {
        var awaitSyntax = (AwaitExpressionSyntax)syntax;
        var awaitedExpression = context.BuildExpression(awaitSyntax.Expression);
        return FunctionCallAst.Basic("CS.await", awaitedExpression);
    }
}
