using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Functions;
using RobloxCS.AST.Parameters;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Types;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2;
using RobloxCS.TranspilerV2.Builders;
using RobloxCS.TranspilerV2.Nodes.Common;
using AstTypeInfo = RobloxCS.AST.Types.TypeInfo;
using FunctionCallAst = RobloxCS.AST.Expressions.FunctionCall;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class UsingStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.UsingStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax syntax)
    {
        var usingStatement = (UsingStatementSyntax)syntax;

        if (!usingStatement.AwaitKeyword.IsKind(SyntaxKind.None))
        {
            throw Logger.UnsupportedError(usingStatement.AwaitKeyword, "await using");
        }

        var statements = new List<Statement>();
        string resourceIdentifier;

        if (usingStatement.Declaration is { } declaration)
        {
            if (declaration.Variables.Count != 1)
            {
                throw Logger.UnsupportedError(declaration, "multiple variables in using declaration");
            }

            var variable = declaration.Variables[0];
            if (variable.Initializer is null)
            {
                throw Logger.UnsupportedError(variable, "using declarations without initializer");
            }

            var initExpression = ExpressionBuilder.BuildFromSyntax(variable.Initializer.Value, context);
            AppendPrerequisites(context, statements);

            var localSymbol = context.Semantics.GetDeclaredSymbol(variable) as ILocalSymbol;
            List<AstTypeInfo> typeInfos = [];
            var annotateTypes = false;

            if (localSymbol is not null)
            {
                var typeInfo = SyntaxUtilities.TypeInfoFromSymbol(localSymbol.Type);
                typeInfos.Add(typeInfo);
                annotateTypes = declaration.Type is not null
                    && !declaration.Type.IsVar
                    && typeInfo is not BasicTypeInfo;
            }

            if (!annotateTypes)
            {
                typeInfos = [];
            }

            var assignment = new LocalAssignment
            {
                Names = [SymbolExpression.FromString(variable.Identifier.ValueText)],
                Expressions = [initExpression],
                Types = typeInfos,
            };

            statements.Add(assignment);
            resourceIdentifier = variable.Identifier.ValueText;
        }
        else if (usingStatement.Expression is { } expressionSyntax)
        {
            var expression = ExpressionBuilder.BuildFromSyntax(expressionSyntax, context);
            AppendPrerequisites(context, statements);

            resourceIdentifier = context.AllocateTempName("using", "resource");
            statements.Add(new LocalAssignment
            {
                Names = [SymbolExpression.FromString(resourceIdentifier)],
                Expressions = [expression],
                Types = [],
            });
        }
        else
        {
            throw new InvalidOperationException("Unsupported using statement structure.");
        }

        var tryBlockSyntax = usingStatement.Statement as BlockSyntax ?? SyntaxFactory.Block(usingStatement.Statement);
        var tryFunction = TryStatementTransformer.CreateAnonymousFunctionFromBlock(tryBlockSyntax, context, out var tryInfo);

        var (finallyFunction, finallyInfo) = CreateDisposeFinallyFunction(resourceIdentifier);

        var tryStatement = TryStatementTransformer.BuildTryCall(
            context,
            tryFunction,
            tryInfo,
            SymbolExpression.FromString("nil"),
            new TranspilationContext.TryScopeInfo(),
            finallyFunction,
            finallyInfo);

        statements.Add(tryStatement);

        var block = Block.Empty();
        foreach (var statement in statements)
        {
            block.AddStatement(statement);
        }

        return DoStatement.FromBlock(block);
    }

    private static void AppendPrerequisites(TranspilationContext context, List<Statement> statements)
    {
        var prerequisites = context.ConsumePrerequisites();
        foreach (var prereq in prerequisites)
        {
            statements.Add(prereq);
        }
    }

    private static (AnonymousFunction Function, TranspilationContext.TryScopeInfo Info) CreateDisposeFinallyFunction(string resourceIdentifier)
    {
        var condition = new BinaryOperatorExpression
        {
            Left = SymbolExpression.FromString(resourceIdentifier),
            Right = SymbolExpression.FromString("nil"),
            Op = BinOp.TildeEqual,
        };

        var disposeCall = FunctionCallAst.Basic($"{resourceIdentifier}:Dispose");
        var disposeStatement = new FunctionCallStatement
        {
            Prefix = disposeCall.Prefix,
            Suffixes = disposeCall.Suffixes,
        };

        var disposeBlock = Block.Empty();
        disposeBlock.AddStatement(disposeStatement);

        var guardBlock = Block.Empty();
        guardBlock.AddStatement(new If
        {
            Condition = condition,
            ThenBody = disposeBlock,
        });

        var function = new AnonymousFunction
        {
            Body = new FunctionBody
            {
                Parameters = new List<Parameter>(),
                TypeSpecifiers = new List<AstTypeInfo>(),
                ReturnType = BasicTypeInfo.Void(),
                Body = guardBlock,
            },
        };

        return (function, new TranspilationContext.TryScopeInfo());
    }
}
