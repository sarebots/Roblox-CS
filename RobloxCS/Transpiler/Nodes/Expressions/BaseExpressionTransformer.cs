using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class BaseExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.BaseExpression, Transform);
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax node)
    {
        return SymbolExpression.FromString("super");
    }
}
