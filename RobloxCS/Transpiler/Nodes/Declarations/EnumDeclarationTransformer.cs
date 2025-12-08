using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Statements;
using RobloxCS.TranspilerV2.Builders;

namespace RobloxCS.TranspilerV2.Nodes.Declarations;

internal static class EnumDeclarationTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterDeclarationTransformer<EnumDeclarationSyntax>(
            SyntaxKind.EnumDeclaration,
            Transform);
    }

    private static IEnumerable<Statement> Transform(TranspilationContext context, EnumDeclarationSyntax node)
    {
        // Legacy generator treats enums as compile-time constants; no Luau emit required.
        yield break;
    }
}
