using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Statements;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class BlockTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.Block, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax node)
    {
        var blockSyntax = (BlockSyntax)node;
        var block = new Block { Statements = new List<Statement>() };
        
        foreach (var stmt in blockSyntax.Statements)
        {
            block.AddStatement(Builders.StatementBuilder.Transpile(stmt, context));
        }
        
        return new DoStatement { Block = block };
    }
}
