using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Expressions;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class BreakStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.BreakStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax node)
    {
        if (context.IsInsideTryScope)
        {
            context.MarkBreakEncountered();
            return Return.FromExpressions([
                SymbolExpression.FromString("CS.TRY_BREAK"),
                FunctionCall.Basic("table.pack"),
            ]);
        }

        return new Break();
    }
}
