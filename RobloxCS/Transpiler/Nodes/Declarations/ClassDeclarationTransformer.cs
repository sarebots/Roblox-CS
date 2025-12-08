using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Statements;
using RobloxCS.TranspilerV2.Builders;

namespace RobloxCS.TranspilerV2.Nodes.Declarations;

internal static class ClassDeclarationTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterDeclarationTransformer<ClassDeclarationSyntax>(
            SyntaxKind.ClassDeclaration,
            Transform);
    }

    private static IEnumerable<Statement> Transform(TranspilationContext context, ClassDeclarationSyntax node)
    {
        var classSymbol = context.Semantics.CheckedGetDeclaredSymbol(node);
        return ClassBuilder.Build(node, classSymbol, context);
    }
}
