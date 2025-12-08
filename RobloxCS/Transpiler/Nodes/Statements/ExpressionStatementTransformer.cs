using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Statements;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class ExpressionStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.ExpressionStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax node)
    {
        var exprStmt = (ExpressionStatementSyntax)node;
        
        if (exprStmt.Expression is AssignmentExpressionSyntax assignment)
        {
            return Nodes.Expressions.AssignmentExpressionTransformer.BuildStatement(context, assignment);
        }
        
        var expression = Builders.ExpressionBuilder.BuildFromSyntax(exprStmt.Expression, context);
        
        if (expression is AST.Expressions.FunctionCall call)
        {
            return new FunctionCallStatement
            {
                Prefix = call.Prefix,
                Suffixes = call.Suffixes
            };
        }
        
        // Comment AST node doesn't exist, return empty block or error?
        // Let's return an empty block with a comment if possible, but Block is a Statement.
        // Or just return a no-op.
        return DoStatement.FromBlock(Block.Empty());
    }
}
