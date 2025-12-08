using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Prefixes;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Suffixes;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2.Builders.Declarations;
using RobloxCS.TranspilerV2.Builders;

namespace RobloxCS.TranspilerV2.Nodes.Declarations;

internal static class StructDeclarationTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterDeclarationTransformer<StructDeclarationSyntax>(
            SyntaxKind.StructDeclaration,
            Transform);
    }

    private static IEnumerable<Statement> Transform(TranspilationContext context, StructDeclarationSyntax node)
    {
        var structSymbol = context.Semantics.CheckedGetDeclaredSymbol(node);
        var structName = context.GetTypeName(structSymbol);

        if (!StructDeclarationBuilder.TryBuild(node, structSymbol, context, out var statements))
        {
            Logger.Warn($"Struct '{structName}' is not yet supported; falling back to class lowering.");
            context.EnsureTypePredeclaration(structName);
            var classSyntax = ConvertStructToClass(node);
            foreach (var statement in ClassBuilder.Build(classSyntax, structSymbol, context))
            {
                yield return statement;
            }
        }
        else
        {
            foreach (var statement in statements)
            {
                yield return statement;
            }
        }
    }

    private static ClassDeclarationSyntax ConvertStructToClass(StructDeclarationSyntax node)
    {
        var members = new List<MemberDeclarationSyntax>();
        if (node.Members.Count > 0)
        {
            members.AddRange(node.Members);
        }

        return SyntaxFactory.ClassDeclaration(
            attributeLists: node.AttributeLists,
            modifiers: node.Modifiers,
            identifier: node.Identifier,
            typeParameterList: node.TypeParameterList,
            baseList: node.BaseList,
            constraintClauses: node.ConstraintClauses,
            members: SyntaxFactory.List(members));
    }
}
