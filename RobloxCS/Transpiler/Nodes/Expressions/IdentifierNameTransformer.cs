using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class IdentifierNameTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.IdentifierName, Transform);
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax node)
    {
        var identifier = (IdentifierNameSyntax)node;
        return SymbolExpression.FromString(identifier.Identifier.Text);
    }
}
