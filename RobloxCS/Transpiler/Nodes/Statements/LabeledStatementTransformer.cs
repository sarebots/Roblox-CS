using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Statements;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class LabeledStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.LabeledStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax node)
    {
        var labeledStatement = (LabeledStatementSyntax)node;
        
        var block = new Block { Statements = new List<Statement>() };
        block.AddStatement(Label.WithName(labeledStatement.Identifier.Text));
        block.AddStatement(Builders.StatementBuilder.Transpile(labeledStatement.Statement, context));
        
        return new DoStatement { Block = block };
    }
}
