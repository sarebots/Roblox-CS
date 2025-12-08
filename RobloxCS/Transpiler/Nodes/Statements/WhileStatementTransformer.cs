using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Statements;
using RobloxCS.TranspilerV2.Builders;
using RobloxCS.TranspilerV2.Nodes.Common;
using RobloxCS.TranspilerV2;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class WhileStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.WhileStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax syntax)
    {
        var whileStatement = (WhileStatementSyntax)syntax;
        var condition = ExpressionBuilder.BuildFromSyntax(whileStatement.Condition, context);

        context.LoopStack.Push(new LoopInfo());
        var body = StatementTransformerUtilities.BuildBlockFromStatement(whileStatement.Statement, context);
        context.LoopStack.Pop();

        return new While
        {
            Condition = condition,
            Body = body,
        };
    }
}
