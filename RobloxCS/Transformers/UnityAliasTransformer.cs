using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.Luau;
using RobloxCS.Shared;

namespace RobloxCS.Transformers;

public sealed class UnityAliasTransformer(FileCompilation file) : BaseTransformer(file)
{
    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        if (node.Initializer == null && IsGameObjectType(node.Type))
        {
            var arguments = (ArgumentListSyntax?)Visit(node.ArgumentList) ?? SyntaxFactory.ArgumentList();
            var invocation = CreateAliasInvocation("CreateGameObject", arguments);
            return invocation.WithTriviaFrom(node);
        }

        return base.VisitObjectCreationExpression(node);
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node.Expression is MemberAccessExpressionSyntax memberAccess
            && TryGetDebugLogAlias(memberAccess, out var aliasMethod))
        {
            var arguments = (ArgumentListSyntax?)Visit(node.ArgumentList) ?? SyntaxFactory.ArgumentList();
            var invocation = CreateAliasInvocation(aliasMethod, arguments);
            return invocation.WithTriviaFrom(node);
        }

        return base.VisitInvocationExpression(node);
    }

    private static InvocationExpressionSyntax CreateAliasInvocation(string methodName, ArgumentListSyntax arguments)
    {
        var robloxName = SyntaxFactory.IdentifierName("Roblox");
        var unityAliases = SyntaxFactory.IdentifierName("UnityAliases");
        var qualifier = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, robloxName, unityAliases);
        var method = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, qualifier, SyntaxFactory.IdentifierName(methodName));
        return SyntaxFactory.InvocationExpression(method, arguments);
    }

    private static bool IsGameObjectType(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            IdentifierNameSyntax { Identifier.Text: "GameObject" } => true,
            QualifiedNameSyntax qualified when qualified.Right.Identifier.Text == "GameObject" => true,
            AliasQualifiedNameSyntax alias when alias.Name.Identifier.Text == "GameObject" => true,
            _ => false
        };
    }

    private static bool TryGetDebugLogAlias(MemberAccessExpressionSyntax memberAccess, out string aliasMethod)
    {
        aliasMethod = string.Empty;
        if (!IsDebugExpression(memberAccess.Expression))
        {
            return false;
        }

        var methodName = memberAccess.Name.Identifier.Text;
        switch (methodName)
        {
            case "Log":
            case "LogWarning":
            case "LogError":
                aliasMethod = methodName;
                return true;
            default:
                return false;
        }
    }

    private static bool IsDebugExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax { Identifier.Text: "Debug" } => true,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text == "Debug",
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text == "Debug",
            _ => false,
        };
    }
}
