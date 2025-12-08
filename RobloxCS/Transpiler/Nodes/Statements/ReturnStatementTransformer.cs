using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Statements;

using RobloxCS.AST.Expressions;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class ReturnStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.ReturnStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax node)
    {
        context.MarkReturnEncountered();
        var returnStatement = (ReturnStatementSyntax)node;
        var expressions = new List<Expression>();
        
        if (returnStatement.Expression != null)
        {
            expressions.Add(Builders.ExpressionBuilder.BuildFromSyntax(returnStatement.Expression, context));
        }
        else
        {
            expressions.Add(SymbolExpression.FromString("nil"));
        }

        return Return.FromExpressions(expressions);
    }
}
