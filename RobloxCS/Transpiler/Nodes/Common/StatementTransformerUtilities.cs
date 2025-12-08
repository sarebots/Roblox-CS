using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Statements;
using RobloxCS.TranspilerV2;
using RobloxCS.TranspilerV2.Builders;

namespace RobloxCS.TranspilerV2.Nodes.Common;

internal static class StatementTransformerUtilities
{
    public static Block BuildBlockFromStatement(StatementSyntax syntax, TranspilationContext context)
    {
        if (syntax is BlockSyntax blockSyntax)
        {
            return BuildBlock(blockSyntax, context);
        }

        var statement = StatementBuilder.Transpile(syntax, context);
        var block = Block.Empty();
        context.AppendPrerequisites(block);
        block.AddStatement(statement);
        return block;
    }

    public static Block BuildBlock(BlockSyntax blockSyntax, TranspilationContext context)
    {
        context.PushScope();
        var block = Block.Empty();

        foreach (var statementSyntax in blockSyntax.Statements)
        {
            var statement = StatementBuilder.Transpile(statementSyntax, context);
            context.AppendPrerequisites(block);
            block.AddStatement(statement);
        }

        context.AppendPrerequisites(block);

        context.PopScope();

        return block;
    }

    public static Block WrapInBlock(Statement statement)
    {
        var block = Block.Empty();
        block.AddStatement(statement);
        return block;
    }
}
