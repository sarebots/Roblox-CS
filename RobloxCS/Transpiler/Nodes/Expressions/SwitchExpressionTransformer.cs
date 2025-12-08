using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Statements;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2.Builders;
using RobloxCS.TranspilerV2.Nodes.Common;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class SwitchExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.SwitchExpression, Transform);
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax syntax)
    {
        var switchExpression = (SwitchExpressionSyntax)syntax;
        var lowering = LowerSwitchExpression(context.TranspilationContext, switchExpression);

        foreach (var statement in lowering.Statements)
        {
            context.TranspilationContext.AddPrerequisite(statement);
        }

        return lowering.ResultExpression;
    }

    internal sealed record SwitchExpressionLoweringResult(List<Statement> Statements, Expression ResultExpression);

    internal static SwitchExpressionLoweringResult LowerSwitchExpression(TranspilationContext transpilationContext, SwitchExpressionSyntax switchExpression)
    {
        var statements = new List<Statement>();
        var transformContext = new TransformContext(
            transpilationContext,
            syntax => ExpressionBuilder.BuildFromSyntax(syntax, transpilationContext),
            syntax => ExpressionBuilder.BuildFromSyntax(syntax, transpilationContext));

        var governingExpressionSyntax = switchExpression.GoverningExpression;
        var governingExpression = transformContext.BuildExpression(governingExpressionSyntax);
        statements.AddRange(transpilationContext.ConsumePrerequisites());

        var requiresComparandAlias = RequiresComparandAlias(switchExpression);
        var needsTempComparand = requiresComparandAlias || NeedsTempComparand(switchExpression.GoverningExpression);

        Expression comparandExpression;
        string? comparandName = null;
        IDisposable? aliasScope = null;
        var governingSymbol = transpilationContext.Semantics.GetSymbolInfo(governingExpressionSyntax).Symbol;

        if (needsTempComparand)
        {
            comparandName = transpilationContext.AllocateTempName("switch", "exp");
            comparandExpression = SymbolExpression.FromString(comparandName);

            statements.Add(new LocalAssignment
            {
                Names = [SymbolExpression.FromString(comparandName)],
                Expressions = [governingExpression],
                Types = [],
            });

            if (requiresComparandAlias && governingSymbol is ILocalSymbol or IParameterSymbol)
            {
                aliasScope = transpilationContext.PushSymbolAlias(governingSymbol, comparandName);
            }

            if (requiresComparandAlias)
            {
                transpilationContext.RegisterListSliceVariable(comparandName);
            }
        }
        else
        {
            comparandExpression = governingExpression;
        }

        try
        {
            var resultName = requiresComparandAlias
                ? transpilationContext.AllocateTempName("switch", "value")
                : transpilationContext.AllocateTempName("newValue");
            var resultSymbol = SymbolExpression.FromString(resultName);

            statements.Add(new LocalAssignment
            {
                Names = [resultSymbol],
                Expressions = [],
                Types = [],
            });

            var loopStatements = BuildLoopStatements(transformContext, switchExpression, comparandExpression, resultName);

            var loopBlock = Block.Empty();
            foreach (var statement in loopStatements)
            {
                loopBlock.AddStatement(statement);
            }

            statements.Add(new Repeat
            {
                Body = loopBlock,
                Condition = new BooleanExpression { Value = true },
            });

            return new SwitchExpressionLoweringResult(statements, resultSymbol);
        }
        finally
        {
            aliasScope?.Dispose();
        }
    }

    private static List<Statement> BuildLoopStatements(
        TransformContext context,
        SwitchExpressionSyntax switchExpression,
        Expression comparandExpression,
        string resultName)
    {
        var statements = new List<Statement>();
        SwitchExpressionArmSyntax? discardArm = null;

        foreach (var arm in switchExpression.Arms)
        {
            if (arm.Pattern is DiscardPatternSyntax)
            {
                discardArm = arm;
                continue;
            }

            var patternMatch = PatternConditionBuilder.Build(context.TranspilationContext, arm.Pattern, comparandExpression);
            var armBlock = Block.Empty();
            foreach (var prereq in patternMatch.Prerequisites)
            {
                armBlock.AddStatement(prereq);
            }

            var condition = patternMatch.Condition;
            Expression? guardExpression = null;
            IReadOnlyList<Statement> guardPrereqs = Array.Empty<Statement>();

            if (arm.WhenClause is { Condition: { } whenCondition })
            {
                guardExpression = context.BuildExpression(whenCondition);
                guardPrereqs = context.TranspilationContext.ConsumePrerequisites();
            }

            var bodyBlock = Block.Empty();

            var valueExpression = context.BuildExpression(arm.Expression);
            var valuePrereqs = context.TranspilationContext.ConsumePrerequisites();
            foreach (var prereq in valuePrereqs)
            {
                bodyBlock.AddStatement(prereq);
            }

            bodyBlock = BuildArmBody(bodyBlock, patternMatch.Bindings, guardExpression, guardPrereqs);

            bodyBlock.AddStatement(new Assignment
            {
                Vars = [VarName.FromString(resultName)],
                Expressions = [valueExpression],
            });

            bodyBlock.AddStatement(new Break());

            armBlock.AddStatement(new If
            {
                Condition = condition,
                ThenBody = bodyBlock,
            });

            Statement armStatement = armBlock.Statements.Count == 1
                ? armBlock.Statements[0]
                : DoStatement.FromBlock(armBlock);

            statements.Add(armStatement);
        }

        if (discardArm is not null)
        {
            var defaultValue = context.BuildExpression(discardArm.Expression);
            var defaultPrereqs = context.TranspilationContext.ConsumePrerequisites();
            foreach (var prereq in defaultPrereqs)
            {
                statements.Add(prereq);
            }

            statements.Add(new Assignment
            {
                Vars = [VarName.FromString(resultName)],
                Expressions = [defaultValue],
            });
        }

        return statements;
    }

    private static Expression CloneExpression(Expression expression) =>
        (Expression)expression.DeepClone();

    private static Block BuildArmBody(
        Block body,
        IReadOnlyList<Statement> bindings,
        Expression? guardExpression,
        IReadOnlyList<Statement> guardPrereqs)
    {
        if (bindings.Count == 0 && guardExpression is null && guardPrereqs.Count == 0)
        {
            return body;
        }

        var block = Block.Empty();
        foreach (var binding in bindings)
        {
            block.AddStatement(binding);
        }

        foreach (var prereq in guardPrereqs)
        {
            block.AddStatement(prereq);
        }

        if (guardExpression is not null)
        {
            block.AddStatement(new If
            {
                Condition = guardExpression,
                ThenBody = CloneBlock(body),
            });
        }
        else
        {
            block.AddBlock(body);
        }

        return block;
    }

    private static Block CloneBlock(Block source)
    {
        var clone = Block.Empty();
        foreach (var statement in source.Statements)
        {
            clone.AddStatement((Statement)statement.DeepClone());
        }

        return clone;
    }

    private static bool NeedsTempComparand(ExpressionSyntax expressionSyntax) =>
        expressionSyntax switch
        {
            IdentifierNameSyntax => false,
            LiteralExpressionSyntax => false,
            _ => true,
        };

    private static bool RequiresComparandAlias(SwitchExpressionSyntax switchExpression)
    {
        foreach (var arm in switchExpression.Arms)
        {
            if (PatternAliasUtilities.PatternRequiresAlias(arm.Pattern))
            {
                return true;
            }
        }

        return false;
    }
}
