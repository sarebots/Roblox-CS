using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Statements;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2;
using RobloxCS.TranspilerV2.Builders;
using RobloxCS.TranspilerV2.Nodes.Common;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class SwitchStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.SwitchStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax syntax)
    {
        var switchStatement = (SwitchStatementSyntax)syntax;

        var patternLabels = switchStatement.Sections
            .SelectMany(section => section.Labels)
            .OfType<CasePatternSwitchLabelSyntax>()
            .ToList();

        var requiresPatternAlias = patternLabels.Any(label => PatternAliasUtilities.PatternRequiresAlias(label.Pattern));
        var createTempComparand = NeedsTempComparand(switchStatement.Expression) || requiresPatternAlias;

        var switchExpression = ExpressionBuilder.BuildFromSyntax(switchStatement.Expression, context);
        Expression comparandExpression;
        string? comparandName = null;
        IDisposable? aliasScope = null;
        var governingSymbol = context.Semantics.GetSymbolInfo(switchStatement.Expression).Symbol;

        if (createTempComparand)
        {
            comparandName = context.AllocateTempName("switch", "exp");
            comparandExpression = SymbolExpression.FromString(comparandName);

            if (requiresPatternAlias && governingSymbol is ILocalSymbol or IParameterSymbol)
            {
                aliasScope = context.PushSymbolAlias(governingSymbol, comparandName);
            }

            if (requiresPatternAlias)
            {
                context.RegisterListSliceVariable(comparandName);
            }
        }
        else
        {
            comparandExpression = switchExpression;
        }

        var requiresFallthrough = switchStatement.Sections.Any(section => section.Labels.Count > 1);
        string? fallthroughName = requiresFallthrough ? context.AllocateTempName("fallthrough") : null;

        var loopBlock = Block.Empty();

        if (requiresFallthrough)
        {
            loopBlock.AddStatement(new LocalAssignment
            {
                Names = [SymbolExpression.FromString(fallthroughName!)],
                Expressions = [new BooleanExpression { Value = false }],
                Types = [],
            });
        }

        Block? defaultBlock = null;

        foreach (var section in switchStatement.Sections)
        {
            var sectionHasFallthrough = section.Labels.Count > 1;

            var sectionStatements = BuildSectionStatements(section, context);

            foreach (var (label, index) in section.Labels.Select((label, idx) => (label, idx)))
            {
                var isLastLabel = index == section.Labels.Count - 1;
                var body = Block.Empty();

                if (!isLastLabel && sectionHasFallthrough)
                {
                    body.AddStatement(new Assignment
                    {
                        Vars = [VarName.FromString(fallthroughName!)],
                        Expressions = [new BooleanExpression { Value = true }],
                    });
                }

                if (isLastLabel)
                {
                    foreach (var statement in sectionStatements)
                    {
                        body.AddStatement(statement);
                    }
                }

                switch (label)
                {
                    case CaseSwitchLabelSyntax caseLabel:
                    {
                        var condition = BuildCaseCondition(caseLabel, comparandExpression, context);
                        AppendPrerequisites(context, loopBlock);

                        if (sectionHasFallthrough)
                        {
                            condition = new BinaryOperatorExpression
                            {
                                Left = SymbolExpression.FromString(fallthroughName!),
                                Op = BinOp.Or,
                                Right = condition,
                            };
                        }

                        loopBlock.AddStatement(new If
                        {
                            Condition = condition,
                            ThenBody = body,
                        });
                        break;
                    }

                    case CasePatternSwitchLabelSyntax patternLabel:
                    {
                        var patternMatch = PatternConditionBuilder.Build(context, patternLabel.Pattern, comparandExpression);
                        var labelBlock = Block.Empty();
                        foreach (var prereq in patternMatch.Prerequisites)
                        {
                            labelBlock.AddStatement(prereq);
                        }

                        var condition = patternMatch.Condition;

                        Expression? guardExpression = null;
                        IReadOnlyList<Statement> guardPrereqs = Array.Empty<Statement>();

                        if (patternLabel.WhenClause is { Condition: { } whenCondition })
                        {
                            guardExpression = ExpressionBuilder.BuildFromSyntax(whenCondition, context);
                            guardPrereqs = context.ConsumePrerequisites();
                        }

                        var fallthroughBody = CloneBlock(body);
                        var patternBody = BuildPatternBody(CloneBlock(body), patternMatch.Bindings, guardExpression, guardPrereqs);

                        if (sectionHasFallthrough)
                        {
                            labelBlock.AddStatement(new If
                            {
                                Condition = SymbolExpression.FromString(fallthroughName!),
                                ThenBody = fallthroughBody,
                            });

                            labelBlock.AddStatement(new If
                            {
                                Condition = condition,
                                ThenBody = patternBody,
                            });
                        }
                        else
                        {
                            labelBlock.AddStatement(new If
                            {
                                Condition = condition,
                                ThenBody = patternBody,
                            });
                        }

                        Statement labelStatement = labelBlock.Statements.Count == 1
                            ? labelBlock.Statements[0]
                            : DoStatement.FromBlock(labelBlock);

                        loopBlock.AddStatement(labelStatement);

                        break;
                    }

                    case DefaultSwitchLabelSyntax:
                    {
                        defaultBlock = MergeBlocks(defaultBlock, body);
                        break;
                    }

                    default:
                        throw Logger.CompilerError($"Unsupported switch label: {label.Kind()}", label);
                }
            }
        }

        if (defaultBlock is not null && defaultBlock.Statements.Count > 0)
        {
            loopBlock.AddStatement(DoStatement.FromBlock(defaultBlock));
        }

        var repeat = new Repeat
        {
            Body = loopBlock,
            Condition = new BooleanExpression { Value = true },
        };

        var finalStatements = new List<Statement>();

        if (createTempComparand)
        {
            finalStatements.Add(new LocalAssignment
            {
                Names = [SymbolExpression.FromString(comparandName!)],
                Expressions = [switchExpression],
                Types = [],
            });
        }

        var prerequisites = context.ConsumePrerequisites();
        finalStatements.AddRange(prerequisites);
        finalStatements.Add(repeat);

        Statement result;
        if (finalStatements.Count == 1)
        {
            result = repeat;
        }
        else
        {
            var block = Block.Empty();
            foreach (var statement in finalStatements)
            {
                block.AddStatement(statement);
            }

            result = DoStatement.FromBlock(block);
        }

        aliasScope?.Dispose();
        return result;
    }

    private static List<Statement> BuildSectionStatements(SwitchSectionSyntax section, TranspilationContext context)
    {
        var statements = new List<Statement>();
        foreach (var statementSyntax in section.Statements)
        {
            var statement = StatementBuilder.Transpile(statementSyntax, context);
            context.AppendPrerequisites(statements);
            statements.Add(statement);
        }

        context.AppendPrerequisites(statements);
        return statements;
    }

    private static BinaryOperatorExpression BuildCaseCondition(
        CaseSwitchLabelSyntax label,
        Expression comparandExpression,
        TranspilationContext context)
    {
        var left = CloneExpression(comparandExpression);
        var right = ExpressionBuilder.BuildFromSyntax(label.Value, context);

        return new BinaryOperatorExpression
        {
            Left = left,
            Right = right,
            Op = BinOp.TwoEqual,
        };
    }

    private static Expression CloneExpression(Expression expression)
    {
        return (Expression)expression.DeepClone();
    }

    private static Block MergeBlocks(Block? existing, Block additional)
    {
        if (existing is null)
        {
            return additional;
        }

        existing.AddBlock(additional);
        return existing;
    }

    private static void AppendPrerequisites(TranspilationContext context, Block target)
    {
        foreach (var prerequisite in context.ConsumePrerequisites())
        {
            target.AddStatement(prerequisite);
        }
    }

    private static Block BuildPatternBody(
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
}
