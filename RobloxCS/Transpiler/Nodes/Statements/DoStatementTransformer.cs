using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Statements;
using RobloxCS.TranspilerV2;
using RobloxCS.TranspilerV2.Builders;
using RobloxCS.TranspilerV2.Nodes.Common;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class DoStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.DoStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax syntax)
    {
        var doStatement = (DoStatementSyntax)syntax;
        var condition = ExpressionBuilder.BuildFromSyntax(doStatement.Condition, context);
        var body = StatementTransformerUtilities.BuildBlockFromStatement(doStatement.Statement, context);

        return new Repeat
        {
            Body = body,
            Condition = new UnaryOperatorExpression
            {
                Op = UnOp.Not,
                Operand = condition,
            },
        };
    }
}
