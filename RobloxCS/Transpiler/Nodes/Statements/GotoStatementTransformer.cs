using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Statements;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class GotoStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.GotoStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax node)
    {
        var gotoStatement = (GotoStatementSyntax)node;
        
        if (gotoStatement.CaseOrDefaultKeyword.Kind() == SyntaxKind.CaseKeyword || 
            gotoStatement.CaseOrDefaultKeyword.Kind() == SyntaxKind.DefaultKeyword)
        {
            // TODO: Handle goto case/default (requires switch statement context)
        }
        else if (gotoStatement.Expression is IdentifierNameSyntax label)
        {
            return Goto.ToLabel(label.Identifier.Text);
        }
        
        return DoStatement.FromBlock(Block.Empty()); // TODO: goto case/default
    }
}
