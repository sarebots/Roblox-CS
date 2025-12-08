using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Statements;
using RobloxCS.TranspilerV2.Builders;

namespace RobloxCS.TranspilerV2.Nodes.Declarations;

internal static class RecordDeclarationTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterDeclarationTransformer<RecordDeclarationSyntax>(
            SyntaxKind.RecordDeclaration,
            Transform);
    }

    private static IEnumerable<Statement> Transform(TranspilationContext context, RecordDeclarationSyntax node)
    {
        var recordSymbol = context.Semantics.CheckedGetDeclaredSymbol(node);
        var classSyntax = ConvertRecordToClass(node);
        return ClassBuilder.Build(classSyntax, recordSymbol, context);
    }

    private static ClassDeclarationSyntax ConvertRecordToClass(RecordDeclarationSyntax node)
    {
        var properties = CreatePropertiesFromParameters(node.ParameterList);
        var constructor = CreateConstructorFromParameters(node.Identifier, node.ParameterList, node.Modifiers);

        var members = new List<MemberDeclarationSyntax>();
        if (constructor is not null)
        {
            members.Add(constructor);
        }
        members.AddRange(properties);
        members.AddRange(node.Members);

        return SyntaxFactory.ClassDeclaration(
            attributeLists: node.AttributeLists,
            modifiers: node.Modifiers,
            identifier: node.Identifier,
            typeParameterList: node.TypeParameterList,
            baseList: node.BaseList,
            constraintClauses: node.ConstraintClauses,
            members: SyntaxFactory.List(members));
    }

    private static ConstructorDeclarationSyntax? CreateConstructorFromParameters(
        SyntaxToken identifier,
        ParameterListSyntax? parameterList,
        SyntaxTokenList modifiers)
    {
        if (parameterList is null || parameterList.Parameters.Count == 0)
        {
            return null;
        }

        var assignments = parameterList.Parameters.Select(parameter =>
            SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.ThisExpression(),
                        SyntaxFactory.IdentifierName(parameter.Identifier)),
                    SyntaxFactory.IdentifierName(parameter.Identifier))));

        return SyntaxFactory.ConstructorDeclaration(identifier)
            .WithModifiers(modifiers.Count > 0 ? modifiers : SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(parameterList)
            .WithBody(SyntaxFactory.Block(assignments));
    }

    private static IEnumerable<PropertyDeclarationSyntax> CreatePropertiesFromParameters(ParameterListSyntax? parameterList)
    {
        if (parameterList is null)
        {
            return Enumerable.Empty<PropertyDeclarationSyntax>();
        }

        return parameterList.Parameters.Select(parameter =>
        {
            var propertyType = parameter.Type ?? SyntaxFactory.IdentifierName("any");

            return SyntaxFactory.PropertyDeclaration(propertyType, parameter.Identifier)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithAccessorList(
                    SyntaxFactory.AccessorList(
                        SyntaxFactory.List(new[]
                        {
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                        })));
        });
    }
}
