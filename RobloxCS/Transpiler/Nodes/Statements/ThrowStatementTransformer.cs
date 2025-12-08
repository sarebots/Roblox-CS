using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Statements;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class ThrowStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.ThrowStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax node)
    {
        var throwStatement = (ThrowStatementSyntax)node;
        
        Expression expression;
        if (throwStatement.Expression != null)
        {
            expression = Builders.ExpressionBuilder.BuildFromSyntax(throwStatement.Expression, context);
        }
        else
        {
            expression = StringExpression.FromString("Rethrow not implemented");
        }

        return new FunctionCallStatement
        {
            Prefix = new AST.Prefixes.NamePrefix { Name = "error" },
            Suffixes = [
                new AST.Suffixes.AnonymousCall {
                    Arguments = new AST.Functions.FunctionArgs {
                        Arguments = [expression]
                    }
                }
            ]
        };
    }
}
