using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Functions;
using RobloxCS.AST.Parameters;
using RobloxCS.AST.Prefixes;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Suffixes;
using RobloxCS.AST.Types;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2.Nodes.Statements;
using AstTypeInfo = RobloxCS.AST.Types.TypeInfo;
using FunctionCallAst = RobloxCS.AST.Expressions.FunctionCall;
using LuauSymbolMetadata = RobloxCS.Luau.SymbolMetadataManager;

namespace RobloxCS.TranspilerV2.Builders;

internal static class GeneratorTransformer
{
    public static void TryConvertGenerator(
        TranspilationContext context,
        IMethodSymbol methodSymbol,
        BlockSyntax? bodySyntax,
        Block bodyBlock,
        ref AstTypeInfo returnType)
    {
        if (bodySyntax is null)
        {
            return;
        }

        var yieldStatements = bodySyntax.DescendantNodes().OfType<YieldStatementSyntax>().ToList();
        if (yieldStatements.Count == 0)
        {
            return;
        }

        if (!TryResolveGeneratorReturn(methodSymbol.ReturnType, out var isEnumerable, out var elementType))
        {
            throw Logger.UnsupportedError(bodySyntax, "yield statements in a non-enumerator method");
        }

        if (ContainsYieldInNestedFunction(bodySyntax))
        {
            throw Logger.UnsupportedError(bodySyntax, "yield statements inside nested functions", useYet: false);
        }

        var generatorStateSymbol = SymbolExpression.FromString(context.AllocateTempName("generator", "state"));
        context.PushGeneratorScope(generatorStateSymbol, false);

        List<Statement> replacementStatements;
        try
        {
            replacementStatements = LowerGeneratorBody(context, bodySyntax, elementType, generatorStateSymbol);
        }
        finally
        {
            context.PopGeneratorScope();
        }

        bodyBlock.Statements.Clear();
        foreach (var statement in replacementStatements)
        {
            bodyBlock.AddStatement(statement);
        }

        returnType = BasicTypeInfo.FromString("CS.IEnumerator<>");

        LuauSymbolMetadata.Get(methodSymbol.ContainingType).GeneratorMethods.Add(methodSymbol);
    }

    private static bool TryResolveGeneratorReturn(
        ITypeSymbol returnType,
        out bool isEnumerable,
        out ITypeSymbol elementType)
    {
        isEnumerable = false;
        elementType = returnType;

        switch (returnType)
        {
            case IArrayTypeSymbol arrayType:
                isEnumerable = true;
                elementType = arrayType.ElementType;
                return true;
            case INamedTypeSymbol namedType when TryMatchGeneratorType(namedType, out isEnumerable, out elementType):
                return true;
        }

        foreach (var @interface in returnType.AllInterfaces.OfType<INamedTypeSymbol>())
        {
            if (TryMatchGeneratorType(@interface, out isEnumerable, out elementType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryMatchGeneratorType(
        INamedTypeSymbol symbol,
        out bool isEnumerable,
        out ITypeSymbol elementType)
    {
        isEnumerable = false;
        elementType = symbol;

        if (symbol.IsGenericType && symbol.TypeArguments.Length == 1)
        {
            var metadataName = symbol.OriginalDefinition.ToDisplayString();
            if (metadataName == "System.Collections.Generic.IEnumerable<T>")
            {
                isEnumerable = true;
                elementType = symbol.TypeArguments[0];
                return true;
            }

            if (metadataName == "System.Collections.Generic.IEnumerator<T>")
            {
                isEnumerable = false;
                elementType = symbol.TypeArguments[0];
                return true;
            }
        }

        return false;
    }

    private static bool ContainsYieldInNestedFunction(BlockSyntax bodySyntax)
    {
        foreach (var node in bodySyntax.DescendantNodes())
        {
            switch (node)
            {
                case MethodDeclarationSyntax:
                case LocalFunctionStatementSyntax:
                case AnonymousMethodExpressionSyntax:
                case ParenthesizedLambdaExpressionSyntax:
                case SimpleLambdaExpressionSyntax:
                    if (node.DescendantNodes().OfType<YieldStatementSyntax>().Any())
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    private static List<Statement> LowerGeneratorBody(
        TranspilationContext context,
        BlockSyntax bodySyntax,
        ITypeSymbol elementType,
        SymbolExpression generatorStateSymbol)
    {
        if (TryLowerImmediateSequence(context, bodySyntax, out var immediateStatements))
        {
            return immediateStatements;
        }

        var statements = new List<Statement>
        {
            new LocalAssignment
            {
                Names = [generatorStateSymbol],
                Expressions = [FunctionCallAst.Basic("CS.Generator.newState")],
                Types = [],
            }
        };

        var enumeratorFunctions = BuildEnumeratorFunctions(context, bodySyntax.Statements, generatorStateSymbol);
        var initializerFunction = CreateInitializerFunction(enumeratorFunctions);

        var enumeratorCall = FunctionCallAst.Basic("CS.Enumerator.new", initializerFunction);

        statements.Add(Return.FromExpressions([enumeratorCall]));

        return statements;
    }

    private static List<Expression> BuildEnumeratorFunctions(
        TranspilationContext context,
        SyntaxList<StatementSyntax> statements,
        SymbolExpression generatorStateSymbol)
    {
        var enumerationBlocks = PartitionBlocks(statements);
        var functions = new List<Expression>(enumerationBlocks.Count);

        foreach (var blockStatements in enumerationBlocks)
        {
            context.PushScope();

            context.PushGeneratorScope(generatorStateSymbol, true);

            var functionBody = new FunctionBody
            {
                Parameters = [NameParameter.FromString("_breakIteration")],
                TypeSpecifiers = [BasicTypeInfo.FromString("any")],
                ReturnType = BasicTypeInfo.Void(),
                Body = LowerEnumerationBlock(context, blockStatements),
            };

            context.PopGeneratorScope();

            functions.Add(new AnonymousFunction { Body = functionBody });

            context.PopScope();
        }

        return functions;
    }

    private static bool TryLowerImmediateSequence(
        TranspilationContext context,
        BlockSyntax bodySyntax,
        out List<Statement> statements)
    {
        statements = null!;

        if (bodySyntax.Statements.Count == 0)
        {
            return false;
        }

        var yieldExpressions = new List<Expression>();

        foreach (var statement in bodySyntax.Statements)
        {
            if (statement is not YieldStatementSyntax { Expression: { } expressionSyntax } yieldStatement)
            {
                return false;
            }

            if (!yieldStatement.ReturnOrBreakKeyword.IsKind(SyntaxKind.ReturnKeyword))
            {
                return false;
            }

            if (!TryBuildSimpleYieldExpression(expressionSyntax, out var expression))
            {
                return false;
            }

            yieldExpressions.Add(expression);
        }

        if (yieldExpressions.Count == 0)
        {
            return false;
        }

        var fields = yieldExpressions
            .Select(expr => (TableField)new NoKey { Expression = expr })
            .ToList();

        var enumeratorCall = FunctionCallAst.Basic("CS.Enumerator.new", new TableConstructor
        {
            Fields = fields,
        });

        statements = new List<Statement>
        {
            Return.FromExpressions([enumeratorCall]),
        };

        return true;
    }

    private static bool TryBuildSimpleYieldExpression(ExpressionSyntax expressionSyntax, out Expression expression)
    {
        switch (expressionSyntax)
        {
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NumericLiteralExpression):
            {
                if (literal.Token.Value is IConvertible convertible)
                {
                    expression = NumberExpression.From(convertible.ToDouble(null));
                    return true;
                }

                break;
            }

            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
            {
                expression = StringExpression.FromString(literal.Token.ValueText);
                return true;
            }

            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression):
                expression = SymbolExpression.FromString("true");
                return true;

            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.FalseLiteralExpression):
                expression = SymbolExpression.FromString("false");
                return true;

            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NullLiteralExpression):
                expression = SymbolExpression.FromString("nil");
                return true;

            case IdentifierNameSyntax identifier:
                expression = SymbolExpression.FromString(identifier.Identifier.ValueText);
                return true;
        }

        expression = null!;
        return false;
    }

    private static Block LowerEnumerationBlock(
        TranspilationContext context,
        IReadOnlyList<StatementSyntax> statements)
    {
        var block = Block.Empty();
        foreach (var statementSyntax in statements)
        {
            var statement = StatementBuilder.Transpile(statementSyntax, context);
            context.AppendPrerequisites(block);
            block.AddStatement(statement);
        }

        AppendPrerequisites(context, block);

        return block;
    }

    private static AnonymousFunction CreateInitializerFunction(List<Expression> enumeratorFunctions)
    {
        var initializerBlock = Block.Empty();

        var tableConstructor = new TableConstructor
        {
            Fields = enumeratorFunctions
                .Select(function => (TableField)new NoKey { Expression = function })
                .ToList(),
        };

        initializerBlock.AddStatement(Return.FromExpressions([tableConstructor]));

        return new AnonymousFunction
        {
            Body = new FunctionBody
            {
                Parameters = [],
                TypeSpecifiers = [],
                ReturnType = BasicTypeInfo.FromString("any"),
                Body = initializerBlock,
            },
        };
    }

    private static List<List<StatementSyntax>> PartitionBlocks(SyntaxList<StatementSyntax> statements)
    {
        var blocks = new List<List<StatementSyntax>>();
        var currentBlock = new List<StatementSyntax>();

        foreach (var statement in statements)
        {
            currentBlock.Add(statement);

            if (statement is YieldStatementSyntax)
            {
                blocks.Add(currentBlock);
                currentBlock = [];
            }
        }

        return blocks;
    }

    private static void AppendPrerequisites(TranspilationContext context, List<Statement> statements)
    {
        foreach (var prerequisite in context.ConsumePrerequisites())
        {
            statements.Add(prerequisite);
        }
    }

    private static void AppendPrerequisites(TranspilationContext context, Block block)
    {
        foreach (var prerequisite in context.ConsumePrerequisites())
        {
            block.AddStatement(prerequisite);
        }
    }
}
