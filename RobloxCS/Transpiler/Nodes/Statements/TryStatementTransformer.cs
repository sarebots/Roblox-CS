using System;
using System.Collections.Generic;
using System.Linq;
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
using RobloxCS.TranspilerV2.Nodes.Common;
using AstTypeInfo = RobloxCS.AST.Types.TypeInfo;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

using FunctionCallAst = RobloxCS.AST.Expressions.FunctionCall;

internal static class TryStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.TryStatement, Transform);
    }

    public static Statement TransformTryStatement(TranspilationContext context, TryStatementSyntax tryStatement)
    {
        if (tryStatement.Catches.Count > 1)
        {
            throw new NotSupportedException("Multiple catch clauses are not supported.");
        }

        if (tryStatement.Catches.Count == 0 && tryStatement.Finally is null)
        {
            throw new NotSupportedException("try must include a catch or finally block.");
        }

        var tryFunction = CreateAnonymousFunctionFromBlock(tryStatement.Block, context, out var tryInfo);

        Expression catchFunction;
        var catchInfo = new TranspilationContext.TryScopeInfo();
        if (tryStatement.Catches.Count == 1)
        {
            catchFunction = CreateCatchFunction(tryStatement.Catches[0], context, out catchInfo);
        }
        else
        {
            catchFunction = SymbolExpression.FromString("nil");
        }

        Expression finallyFunction;
        var finallyInfo = new TranspilationContext.TryScopeInfo();
        if (tryStatement.Finally is not null)
        {
            finallyFunction = CreateAnonymousFunctionFromBlock(tryStatement.Finally.Block, context, out finallyInfo);
        }
        else
        {
            finallyFunction = SymbolExpression.FromString("nil");
        }

        return BuildTryCall(context, tryFunction, tryInfo, catchFunction, catchInfo, finallyFunction, finallyInfo);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax syntax)
    {
        return TransformTryStatement(context, (TryStatementSyntax)syntax);
    }

    internal static AnonymousFunction CreateAnonymousFunctionFromBlock(
        BlockSyntax blockSyntax,
        TranspilationContext context,
        out TranspilationContext.TryScopeInfo tryInfo)
    {
        context.PushTryScope();
        var block = StatementTransformerUtilities.BuildBlock(blockSyntax, context);
        tryInfo = context.PopTryScope();

        if (ContainsBreakStatement(blockSyntax))
        {
            tryInfo.UsesBreak = true;
        }

        if (ContainsContinueStatement(blockSyntax))
        {
            tryInfo.UsesContinue = true;
        }

        AppendDefaultTryReturn(block);

        return new AnonymousFunction
        {
            Body = new FunctionBody
            {
                Parameters = new List<Parameter>(),
                TypeSpecifiers = new List<AstTypeInfo>(),
                ReturnType = BasicTypeInfo.Void(),
                Body = block,
            },
        };
    }

    private static Expression CreateCatchFunction(
        CatchClauseSyntax catchClause,
        TranspilationContext context,
        out TranspilationContext.TryScopeInfo tryInfo)
    {
        context.PushTryScope();

        var parameters = new List<Parameter>();
        var typeSpecifiers = new List<AstTypeInfo>();

        if (catchClause.Declaration is { Identifier.ValueText: var identifier } declaration)
        {
            parameters.Add(NameParameter.FromString(identifier));

            var declaredType = declaration.Type is null
                ? null
                : context.Semantics.GetTypeInfo(declaration.Type).Type;

            if (declaredType is not null)
            {
                typeSpecifiers.Add(SyntaxUtilities.TypeInfoFromSymbol(declaredType));
            }
            else
            {
                typeSpecifiers.Add(BasicTypeInfo.FromString("any"));
            }
        }

        var block = StatementTransformerUtilities.BuildBlock(catchClause.Block, context);
        tryInfo = context.PopTryScope();

        if (ContainsBreakStatement(catchClause.Block))
        {
            tryInfo.UsesBreak = true;
        }

        if (ContainsContinueStatement(catchClause.Block))
        {
            tryInfo.UsesContinue = true;
        }

        AppendDefaultTryReturn(block);

        return new AnonymousFunction
        {
            Body = new FunctionBody
            {
                Parameters = parameters,
                TypeSpecifiers = typeSpecifiers,
                ReturnType = BasicTypeInfo.Void(),
                Body = block,
            },
        };
    }

    internal static Statement BuildTryCall(
        TranspilationContext context,
        AnonymousFunction tryFunction,
        TranspilationContext.TryScopeInfo tryInfo,
        Expression catchFunction,
        TranspilationContext.TryScopeInfo catchInfo,
        Expression finallyFunction,
        TranspilationContext.TryScopeInfo finallyInfo)
    {
        var call = FunctionCallAst.Basic("CS.try", tryFunction, catchFunction, finallyFunction);

        if (context.IsInsideLoop)
        {
            context.LoopStack.Peek().ContainsTry = true;
        }

        var requiresControlFlow = tryInfo.HasControlFlow || catchInfo.HasControlFlow || finallyInfo.HasControlFlow;

        if (requiresControlFlow)
        {
            return CreateControlFlowPropagation(call, context.IsInsideLoop);
        }

        return new FunctionCallStatement
        {
            Prefix = call.Prefix,
            Suffixes = call.Suffixes,
        };
    }

    private static Statement CreateControlFlowPropagation(FunctionCallAst call, bool insideLoop)
    {
        const string exitVarName = "__tryExitType";
        const string returnsVarName = "__tryReturns";

        var block = Block.Empty();
        block.AddStatement(new LocalAssignment
        {
            Names =
            [
                SymbolExpression.FromString(exitVarName),
                SymbolExpression.FromString(returnsVarName),
            ],
            Expressions = [call],
            Types = [],
        });

        var returnCondition = new BinaryOperatorExpression
        {
            Left = SymbolExpression.FromString(exitVarName),
            Right = SymbolExpression.FromString("CS.TRY_RETURN"),
            Op = BinOp.TwoEqual,
        };

        var thenBlock = Block.Empty();
        thenBlock.AddStatement(Return.FromExpressions([
            FunctionCallAst.Basic(
                "table.unpack",
                SymbolExpression.FromString(returnsVarName),
                NumberExpression.From(1),
                SymbolExpression.FromString($"{returnsVarName}.n")
            ),
        ]));

        Block? elseBlock = null;

        if (insideLoop)
        {
            var continueCondition = new BinaryOperatorExpression
            {
                Left = SymbolExpression.FromString(exitVarName),
                Right = SymbolExpression.FromString("CS.TRY_CONTINUE"),
                Op = BinOp.TwoEqual,
            };

            var continueBlock = Block.Empty();
            continueBlock.AddStatement(new Continue());

            var continueIf = new If
            {
                Condition = continueCondition,
                ThenBody = continueBlock,
                ElseBody = null,
            };

            var breakCondition = new BinaryOperatorExpression
            {
                Left = SymbolExpression.FromString(exitVarName),
                Right = SymbolExpression.FromString("CS.TRY_BREAK"),
                Op = BinOp.TwoEqual,
            };

            var breakBlock = Block.Empty();
            breakBlock.AddStatement(new Break());

            var breakIf = new If
            {
                Condition = breakCondition,
                ThenBody = breakBlock,
                ElseBody = StatementTransformerUtilities.WrapInBlock(continueIf),
            };

            elseBlock = StatementTransformerUtilities.WrapInBlock(breakIf);
        }

        block.AddStatement(new If
        {
            Condition = returnCondition,
            ThenBody = thenBlock,
            ElseBody = elseBlock,
        });

        var exitNotNil = new BinaryOperatorExpression
        {
            Left = SymbolExpression.FromString(exitVarName),
            Right = SymbolExpression.FromString("nil"),
            Op = BinOp.TildeEqual,
        };

        var propagateBlock = Block.Empty();
        propagateBlock.AddStatement(Return.FromExpressions([
            SymbolExpression.FromString(exitVarName),
            SymbolExpression.FromString(returnsVarName),
        ]));

        block.AddStatement(new If
        {
            Condition = exitNotNil,
            ThenBody = propagateBlock,
            ElseBody = null,
        });

        return DoStatement.FromBlock(block);
    }

    private static void AppendDefaultTryReturn(Block block)
    {
        if (block.Statements.Count == 0 || block.Statements[^1] is not Return)
        {
            block.AddStatement(Return.FromExpressions([
                SymbolExpression.FromString("nil"),
                FunctionCallAst.Basic("table.pack"),
            ]));
        }
    }

    private static bool ContainsBreakStatement(CSharpSyntaxNode node) =>
        node.DescendantNodes().Any(n => n is BreakStatementSyntax);

    private static bool ContainsContinueStatement(CSharpSyntaxNode node) =>
        node.DescendantNodes().Any(n => n is ContinueStatementSyntax);
}
