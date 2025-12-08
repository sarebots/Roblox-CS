using System;
using System.Collections.Generic;
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

using FunctionCallAst = RobloxCS.AST.Expressions.FunctionCall;

internal static class ForEachStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.ForEachStatement, TransformForEach);
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.ForEachVariableStatement, TransformForEachVariable);
    }

    private static Statement TransformForEach(TranspilationContext context, StatementSyntax syntax)
    {
        var forEach = (ForEachStatementSyntax)syntax;

        if (TryBuildRangeNumericFor(forEach, context, out var numericFor))
        {
            return numericFor;
        }

        var expressionType = context.Semantics.GetTypeInfo(forEach.Expression).Type;
        var isString = IsStringType(expressionType);
        var isList = IsListLike(expressionType);
        var useIpairs = isList && !ExpressionBuilder.IsDictionaryType(expressionType) && !isString;

        var iterable = ExpressionBuilder.BuildFromSyntax(forEach.Expression, context);

        context.LoopStack.Push(new LoopInfo());
        var body = StatementTransformerUtilities.BuildBlockFromStatement(forEach.Statement, context);
        context.LoopStack.Pop();

        if (ExpressionBuilder.IsDictionaryType(expressionType))
        {
            var keyTemp = context.AllocateTempName(forEach.Identifier.ValueText, "key");
            var valueTemp = context.AllocateTempName(forEach.Identifier.ValueText, "value");

            var loopBody = Block.Empty();
            loopBody.AddStatement(new LocalAssignment
            {
                Names = [SymbolExpression.FromString(forEach.Identifier.ValueText)],
                Types = [],
                Expressions =
                [
                    new TableConstructor
                    {
                        Fields =
                        [
                            new NameKey { Key = "Key", Value = SymbolExpression.FromString(keyTemp) },
                            new NameKey { Key = "Value", Value = SymbolExpression.FromString(valueTemp) },
                        ],
                    },
                ],
            });
            loopBody.AddBlock(body);

            return new GenericFor
            {
                Names = [VarName.FromString(keyTemp), VarName.FromString(valueTemp)],
                Expressions = [FunctionCallAst.Basic("pairs", iterable)],
                Body = loopBody,
            };
        }

        var names = new List<Var>();
        if (useIpairs)
        {
            names.Add(VarName.FromString("_"));
        }

        names.Add(VarName.FromString(forEach.Identifier.ValueText));

        if (useIpairs)
        {
            iterable = FunctionCallAst.Basic("ipairs", iterable);
        }
        else if (isString)
        {
            iterable = FunctionCallAst.Basic("string.gmatch", iterable, StringExpression.FromString("."));
        }

        return new GenericFor
        {
            Names = names,
            Expressions = [iterable],
            Body = body,
        };
    }

    private static Statement TransformForEachVariable(TranspilationContext context, StatementSyntax syntax)
    {
        var forEach = (ForEachVariableStatementSyntax)syntax;
        var names = ExtractForEachVariables(forEach.Variable);
        if (names.Count == 0)
        {
            throw new NotSupportedException("Unsupported foreach variable pattern.");
        }

        var iterableType = context.Semantics.GetTypeInfo(forEach.Expression).Type;
        var iterable = ExpressionBuilder.BuildFromSyntax(forEach.Expression, context);
        var isString = IsStringType(iterableType);
        var useIpairs = IsListLike(iterableType) && !isString;
        var usePairs = ExpressionBuilder.IsDictionaryType(iterableType);

        if (usePairs && names.Count > 1)
        {
            iterable = FunctionCallAst.Basic("pairs", iterable);
        }
        else if (useIpairs)
        {
            iterable = FunctionCallAst.Basic("ipairs", iterable);
        }
        else if (isString)
        {
            if (names.Count > 1)
            {
                throw new NotSupportedException("String iteration does not support tuple destructuring.");
            }

            iterable = FunctionCallAst.Basic("string.gmatch", iterable, StringExpression.FromString("."));
        }

        context.LoopStack.Push(new LoopInfo());
        var body = StatementTransformerUtilities.BuildBlockFromStatement(forEach.Statement, context);
        context.LoopStack.Pop();

        return new GenericFor
        {
            Names = names,
            Expressions = [iterable],
            Body = body,
        };
    }

    private static bool TryBuildRangeNumericFor(
        ForEachStatementSyntax forEach,
        TranspilationContext context,
        out Statement statement)
    {
        statement = null!;

        if (forEach.Expression is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        if (context.Semantics.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        if (!IsRangeInvocationSymbol(methodSymbol))
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count < 2)
        {
            return false;
        }

        var startExpression = ExpressionBuilder.BuildFromSyntax(arguments[0].Expression, context);
        var endExpression = ExpressionBuilder.BuildFromSyntax(arguments[1].Expression, context);

        Expression stepExpression;
        if (arguments.Count >= 3)
        {
            var rawStep = ExpressionBuilder.BuildFromSyntax(arguments[2].Expression, context);
            stepExpression = IsNumericLiteral(rawStep)
                ? rawStep
                : new BinaryOperatorExpression
                {
                    Left = rawStep,
                    Op = BinOp.Or,
                    Right = NumberExpression.From(1),
                };
        }
        else
        {
            stepExpression = NumberExpression.From(1);
        }

        context.LoopStack.Push(new LoopInfo());
        var body = StatementTransformerUtilities.BuildBlockFromStatement(forEach.Statement, context);
        context.LoopStack.Pop();

        statement = new NumericFor
        {
            Name = VarName.FromString(forEach.Identifier.ValueText),
            Start = startExpression,
            End = endExpression,
            Step = stepExpression,
            Body = body,
        };

        return true;
    }

    private static bool IsRangeInvocationSymbol(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.ContainingType is not { } containingType)
        {
            return false;
        }

        var namespaceName = containingType.ContainingNamespace?.ToDisplayString();
        if (!string.Equals(namespaceName, "Roblox", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(methodSymbol.Name, "range", StringComparison.Ordinal)
            && string.Equals(containingType.Name, "Globals", StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(methodSymbol.Name, "Range", StringComparison.Ordinal)
            && (string.Equals(containingType.Name, "RangeHelper", StringComparison.Ordinal)
                || string.Equals(containingType.Name, "Range", StringComparison.Ordinal));
    }

    private static bool IsNumericLiteral(Expression expression) =>
        expression switch
        {
            NumberExpression => true,
            UnaryOperatorExpression { Op: UnOp.Minus, Operand: NumberExpression } => true,
            _ => false,
        };

    private static bool IsListLike(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is null || IsStringType(typeSymbol))
        {
            return false;
        }

        return StandardUtility.DoesTypeInheritFrom(typeSymbol, "Array")
            || StandardUtility.DoesTypeInheritFrom(typeSymbol, "IEnumerable");
    }

    private static bool IsStringType(ITypeSymbol? typeSymbol) =>
        typeSymbol?.SpecialType == SpecialType.System_String;

    private static List<Var> ExtractForEachVariables(ExpressionSyntax syntax)
    {
        return syntax switch
        {
            DeclarationExpressionSyntax declaration => ExtractFromDesignation(declaration.Designation),
            IdentifierNameSyntax identifier => [VarName.FromString(identifier.Identifier.ValueText)],
            _ => throw new NotSupportedException($"Unsupported foreach variable syntax: {syntax.Kind()}"),
        };
    }

    private static List<Var> ExtractFromDesignation(VariableDesignationSyntax designation)
    {
        var names = new List<Var>();
        AppendDesignation(designation, names);
        return names;
    }

    private static void AppendDesignation(VariableDesignationSyntax designation, List<Var> names)
    {
        switch (designation)
        {
            case SingleVariableDesignationSyntax single:
                names.Add(VarName.FromString(single.Identifier.ValueText));
                break;

            case DiscardDesignationSyntax:
                names.Add(VarName.FromString("_"));
                break;

            case ParenthesizedVariableDesignationSyntax parenthesized:
                foreach (var variable in parenthesized.Variables)
                {
                    AppendDesignation(variable, names);
                }

                break;

            default:
                throw new NotSupportedException($"Unsupported variable designation: {designation.Kind()}");
        }
    }
}
