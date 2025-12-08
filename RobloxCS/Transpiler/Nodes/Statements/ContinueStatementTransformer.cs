using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Statements;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class ContinueStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.ContinueStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax node)
    {
        // Lua doesn't have 'continue' until recently (Luau does).
        // Roblox Luau supports 'continue'.
        context.MarkContinueEncountered();
        return new Continue();
    }
}
