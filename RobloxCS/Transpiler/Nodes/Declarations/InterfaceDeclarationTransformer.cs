using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Statements;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2.Builders.Declarations;

namespace RobloxCS.TranspilerV2.Nodes.Declarations;

internal static class InterfaceDeclarationTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterDeclarationTransformer<InterfaceDeclarationSyntax>(
            SyntaxKind.InterfaceDeclaration,
            Transform);
    }

    private static IEnumerable<Statement> Transform(TranspilationContext context, InterfaceDeclarationSyntax node)
    {
        var interfaceSymbol = context.Semantics.CheckedGetDeclaredSymbol(node);
        var metadata = InterfaceDeclarationBuilder.Analyze(node, interfaceSymbol, context);

        if (InterfaceDeclarationBuilder.TryBuildInterfaceAlias(metadata, out var statements))
        {
            foreach (var statement in statements)
            {
                yield return statement;
            }

            yield break;
        }

        Logger.Warn($"Interface '{metadata.Name}' is not yet supported; emitting placeholder.");
        yield return InterfaceDeclarationBuilder.CreatePlaceholderComment(metadata);
    }
}
