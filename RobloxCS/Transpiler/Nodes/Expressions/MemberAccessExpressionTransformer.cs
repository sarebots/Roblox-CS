using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class MemberAccessExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.SimpleMemberAccessExpression, Transform);
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax node)
    {
        var memberAccess = (MemberAccessExpressionSyntax)node;
        
        var expression = context.BuildExpression(memberAccess.Expression);
        var name = memberAccess.Name.Identifier.Text;
        
        return new IndexExpression
        {
            Target = expression,
            Index = StringExpression.FromString(name)
        };
    }
}
