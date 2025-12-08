using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Expressions;
using RobloxCS.TranspilerV2.Builders;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class MethodGroupExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.IdentifierName, TransformIdentifier);
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.SimpleMemberAccessExpression, TransformMemberAccess);
    }

    private static Expression? TransformIdentifier(TransformContext context, ExpressionSyntax syntax)
    {
        if (syntax is not IdentifierNameSyntax identifier)
        {
            return null;
        }

        if (identifier.SyntaxTree != context.TranspilationContext.Root.SyntaxTree)
        {
            return null;
        }

        if (!ExpressionBuilder.IsMethodGroupContext(identifier))
        {
            return null;
        }

        if (context.SemanticModel.GetSymbolInfo(identifier).Symbol is not IMethodSymbol methodSymbol)
        {
            return null;
        }

        return ExpressionBuilder.BuildMethodGroupExpression(identifier, methodSymbol, context.TranspilationContext);
    }

    private static Expression? TransformMemberAccess(TransformContext context, ExpressionSyntax syntax)
    {
        if (syntax is not MemberAccessExpressionSyntax memberAccess)
        {
            return null;
        }

        if (memberAccess.SyntaxTree != context.TranspilationContext.Root.SyntaxTree)
        {
            return null;
        }

        if (!ExpressionBuilder.IsMethodGroupContext(memberAccess))
        {
            return null;
        }

        if (context.SemanticModel.GetSymbolInfo(memberAccess).Symbol is not IMethodSymbol methodSymbol)
        {
            return null;
        }

        return ExpressionBuilder.BuildMethodGroupExpression(memberAccess, methodSymbol, context.TranspilationContext);
    }
}
