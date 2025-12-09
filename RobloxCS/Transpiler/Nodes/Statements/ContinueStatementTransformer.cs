using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Expressions;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class ContinueStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.ContinueStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax node)
    {
        if (context.IsInsideTryScope)
        {
            context.MarkContinueEncountered();
            return Return.FromExpressions([
                SymbolExpression.FromString("CS.TRY_CONTINUE"),
                FunctionCall.Basic("table.pack"),
            ]);
        }

        // Roblox Luau supports 'continue'.
        return new Continue();
    }
}
