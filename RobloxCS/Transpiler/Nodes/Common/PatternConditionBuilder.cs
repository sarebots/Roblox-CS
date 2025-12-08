using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Types;
using RobloxCS.Renderer;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2;
using RobloxCS.TranspilerV2.Builders;

namespace RobloxCS.TranspilerV2.Nodes.Common;

using FunctionCallAst = RobloxCS.AST.Expressions.FunctionCall;
using AstTypeInfo = RobloxCS.AST.Types.TypeInfo;

internal static class PatternConditionBuilder
{
    private const string TuplePatternMetadataName = "Microsoft.CodeAnalysis.CSharp.Syntax.TuplePatternSyntax";

    internal static PatternMatchResult Build(
        TranspilationContext context,
        PatternSyntax pattern,
        Expression comparandExpression)
    {
        if (pattern is DiscardPatternSyntax)
        {
            return new PatternMatchResult(
                new BooleanExpression { Value = true },
                Array.Empty<Statement>(),
                Array.Empty<Statement>());
        }

        if (IsTuplePatternSyntax(pattern))
        {
            return BuildTuplePattern(context, pattern, comparandExpression);
        }

        return pattern switch
        {
            DeclarationPatternSyntax declarationPattern => BuildDeclarationPattern(context, declarationPattern, comparandExpression),
            TypePatternSyntax typePattern => BuildTypePattern(context, typePattern, comparandExpression),
            RelationalPatternSyntax relationalPattern => BuildRelationalPattern(context, relationalPattern, comparandExpression),
            BinaryPatternSyntax binaryPattern => BuildBinaryPattern(context, binaryPattern, comparandExpression),
            ConstantPatternSyntax constantPattern => BuildConstantPattern(context, constantPattern, comparandExpression),
            VarPatternSyntax varPattern => BuildVarPattern(context, varPattern, comparandExpression),
            ListPatternSyntax listPattern => BuildListPattern(context, listPattern, comparandExpression),
            RecursivePatternSyntax recursivePattern => BuildRecursivePattern(context, recursivePattern, comparandExpression),
            ParenthesizedPatternSyntax parenthesizedPattern => Build(context, parenthesizedPattern.Pattern, comparandExpression),
            _ => throw Logger.UnsupportedError(pattern, $"pattern '{pattern.Kind()}'"),
        };
    }

    private static PatternMatchResult BuildDeclarationPattern(
        TranspilationContext context,
        DeclarationPatternSyntax pattern,
        Expression comparandExpression)
    {
        var typeSymbol = context.Semantics.GetTypeInfo(pattern.Type).Type ?? throw Logger.UnsupportedError(pattern, "pattern type");
        var condition = BuildTypeCheck(context, comparandExpression, typeSymbol);

        var bindings = new List<Statement>();
        var captureAssignment = TryBuildCaptureAssignment(
            context,
            pattern.Designation,
            comparandExpression,
            typeSymbol,
            annotate: true);

        if (captureAssignment is not null)
        {
            bindings.Add(captureAssignment);
        }

        return new PatternMatchResult(condition, Array.Empty<Statement>(), bindings);
    }

    private static PatternMatchResult BuildTypePattern(
        TranspilationContext context,
        TypePatternSyntax pattern,
        Expression comparandExpression)
    {
        var typeSymbol = context.Semantics.GetTypeInfo(pattern.Type).Type ?? throw Logger.UnsupportedError(pattern, "pattern type");
        var condition = BuildTypeCheck(context, comparandExpression, typeSymbol);
        return new PatternMatchResult(condition, Array.Empty<Statement>(), Array.Empty<Statement>());
    }

    private static PatternMatchResult BuildRelationalPattern(
        TranspilationContext context,
        RelationalPatternSyntax pattern,
        Expression comparandExpression)
    {
        var rightExpression = ExpressionBuilder.BuildFromSyntax(pattern.Expression, context);
        var prerequisites = context.ConsumePrerequisites();
        var op = SyntaxUtilities.SyntaxTokenToBinOp(pattern.OperatorToken);

        var condition = new BinaryOperatorExpression
        {
            Left = CloneExpression(comparandExpression),
            Right = rightExpression,
            Op = op,
        };

        return new PatternMatchResult(condition, prerequisites, Array.Empty<Statement>());
    }

    private static PatternMatchResult BuildConstantPattern(
        TranspilationContext context,
        ConstantPatternSyntax pattern,
        Expression comparandExpression)
    {
        var constantExpression = ExpressionBuilder.BuildFromSyntax(pattern.Expression, context);
        var prerequisites = context.ConsumePrerequisites();

        var condition = new BinaryOperatorExpression
        {
            Left = CloneExpression(comparandExpression),
            Right = constantExpression,
            Op = BinOp.TwoEqual,
        };

        return new PatternMatchResult(condition, prerequisites, Array.Empty<Statement>());
    }

    private static PatternMatchResult BuildBinaryPattern(
        TranspilationContext context,
        BinaryPatternSyntax pattern,
        Expression comparandExpression)
    {
        var leftMatch = Build(context, pattern.Left, comparandExpression);
        var rightMatch = Build(context, pattern.Right, comparandExpression);

        BinOp op = pattern.OperatorToken.Kind() switch
        {
            SyntaxKind.AndKeyword => BinOp.And,
            SyntaxKind.OrKeyword => BinOp.Or,
            _ => throw Logger.UnsupportedError(pattern, $"binary pattern operator '{pattern.OperatorToken.Text}'"),
        };

        var combinedCondition = new BinaryOperatorExpression
        {
            Left = leftMatch.Condition,
            Op = op,
            Right = rightMatch.Condition,
        };

        var combinedPrereqs = new List<Statement>(leftMatch.Prerequisites.Count + rightMatch.Prerequisites.Count);
        combinedPrereqs.AddRange(leftMatch.Prerequisites);
        combinedPrereqs.AddRange(rightMatch.Prerequisites);

        var combinedBindings = new List<Statement>(leftMatch.Bindings.Count + rightMatch.Bindings.Count);
        combinedBindings.AddRange(leftMatch.Bindings);
        combinedBindings.AddRange(rightMatch.Bindings);

        return new PatternMatchResult(combinedCondition, combinedPrereqs, combinedBindings);
    }

    private static PatternMatchResult BuildListPattern(
        TranspilationContext context,
        ListPatternSyntax pattern,
        Expression comparandExpression)
    {
        var typeInfo = context.Semantics.GetTypeInfo(pattern);
        var comparandType = typeInfo.ConvertedType ?? typeInfo.Type;

        if (!IsSupportedListType(comparandType, context))
        {
            throw Logger.UnsupportedError(pattern, "list patterns require array or IList comparands.");
        }

        var headPatterns = new List<PatternSyntax>();
        var tailPatterns = new List<PatternSyntax>();
        SlicePatternSyntax? slicePattern = null;

        foreach (var elementPattern in pattern.Patterns)
        {
            if (elementPattern is SlicePatternSyntax slice)
            {
                if (slicePattern is not null)
                {
                    throw Logger.UnsupportedError(pattern, "list patterns support at most one slice pattern.");
                }

                slicePattern = slice;
                continue;
            }

            if (slicePattern is null)
            {
                headPatterns.Add(elementPattern);
            }
            else
            {
                tailPatterns.Add(elementPattern);
            }
        }

        var conditions = new List<Expression>
        {
            new BinaryOperatorExpression
            {
                Left = CloneExpression(comparandExpression),
                Op = BinOp.TildeEqual,
                Right = SymbolExpression.FromString("nil"),
            },
        };

        var requiredCount = headPatterns.Count + tailPatterns.Count;

        conditions.Add(new BinaryOperatorExpression
        {
            Left = CreateLengthExpression(comparandExpression),
            Op = slicePattern is null ? BinOp.TwoEqual : BinOp.GreaterThanEqual,
            Right = NumberExpression.From(requiredCount),
        });

        var prerequisites = new List<Statement>();
        var bindings = new List<Statement>();

        for (var index = 0; index < headPatterns.Count; index++)
        {
            var elementPattern = headPatterns[index];
            var elementAccess = new IndexExpression
            {
                Target = CloneExpression(comparandExpression),
                Index = NumberExpression.From(index + 1),
            };

            var elementMatch = Build(context, elementPattern, elementAccess);
            prerequisites.AddRange(elementMatch.Prerequisites);
            conditions.Add(elementMatch.Condition);
            bindings.AddRange(elementMatch.Bindings);
        }

        for (var index = 0; index < tailPatterns.Count; index++)
        {
            var elementPattern = tailPatterns[index];
            var offsetFromEnd = tailPatterns.Count - (index + 1);

            Expression tailIndex = CreateLengthExpression(comparandExpression);
            if (offsetFromEnd > 0)
            {
                tailIndex = new BinaryOperatorExpression
                {
                    Left = tailIndex,
                    Op = BinOp.Minus,
                    Right = NumberExpression.From(offsetFromEnd),
                };
            }

            var elementAccess = new IndexExpression
            {
                Target = CloneExpression(comparandExpression),
                Index = tailIndex,
            };

            var elementMatch = Build(context, elementPattern, elementAccess);
            prerequisites.AddRange(elementMatch.Prerequisites);
            conditions.Add(elementMatch.Condition);
            bindings.AddRange(elementMatch.Bindings);
        }

        if (slicePattern is not null && slicePattern.Pattern is not null)
        {
            var slicePatternSyntax = slicePattern;
            var captureAssignment = TryBuildSliceCaptureAssignment(
                context,
                slicePatternSyntax.Pattern,
                comparandExpression,
                headPatterns.Count,
                tailPatterns.Count);

            if (captureAssignment is not null)
            {
                bindings.Add(captureAssignment);
            }
        }

        var combinedCondition = CombineConditions(conditions);
        return new PatternMatchResult(combinedCondition, prerequisites, bindings);
    }

    private static PatternMatchResult BuildTuplePattern(
        TranspilationContext context,
        PatternSyntax pattern,
        Expression comparandExpression)
    {
        if (!TryGetTupleSubpatterns(pattern, out var subpatterns))
        {
            throw Logger.UnsupportedError(pattern, "tuple patterns require a newer Roslyn compiler package.");
        }

        var conditions = new List<Expression>();
        var prerequisites = new List<Statement>();
        var bindings = new List<Statement>();

        for (var index = 0; index < subpatterns.Count; index++)
        {
            var subpattern = subpatterns[index];
            var elementExpression = CreateTupleElementAccessExpression(comparandExpression, index + 1);
            var elementMatch = Build(context, subpattern.Pattern, elementExpression);

            prerequisites.AddRange(elementMatch.Prerequisites);
            conditions.Add(elementMatch.Condition);
            bindings.AddRange(elementMatch.Bindings);
        }

        return new PatternMatchResult(CombineConditions(conditions), prerequisites, bindings);
    }

    private static bool IsTuplePatternSyntax(PatternSyntax pattern)
    {
        return string.Equals(pattern.GetType().FullName, TuplePatternMetadataName, StringComparison.Ordinal);
    }

    private static bool TryGetTupleSubpatterns(PatternSyntax pattern, out SeparatedSyntaxList<SubpatternSyntax> subpatterns)
    {
        subpatterns = default;
        var patternType = pattern.GetType();

        if (!string.Equals(patternType.FullName, TuplePatternMetadataName, StringComparison.Ordinal))
        {
            return false;
        }

        var property = patternType.GetProperty("Subpatterns");
        if (property is null)
        {
            return false;
        }

        if (property.GetValue(pattern) is SeparatedSyntaxList<SubpatternSyntax> separatedList)
        {
            subpatterns = separatedList;
            return true;
        }

        return false;
    }

    private static PatternMatchResult BuildRecursivePattern(
        TranspilationContext context,
        RecursivePatternSyntax pattern,
        Expression comparandExpression)
    {
        var conditions = new List<Expression>();
        var prerequisites = new List<Statement>();
        var bindings = new List<Statement>();

        ITypeSymbol? resolvedType = null;

        if (pattern.Type is not null)
        {
            resolvedType = context.Semantics.GetTypeInfo(pattern.Type).Type ?? throw Logger.UnsupportedError(pattern.Type, "pattern type");
            conditions.Add(BuildTypeCheck(context, comparandExpression, resolvedType));
        }

        if (pattern.PropertyPatternClause is { } propertyClause)
        {
            foreach (var subpattern in propertyClause.Subpatterns)
            {
                var propertyName = GetPropertySubpatternName(subpattern) ?? throw Logger.UnsupportedError(subpattern, "property subpattern name");

                var propertyExpression = CreatePropertyAccessExpression(comparandExpression, propertyName);
                var propertyMatch = Build(context, subpattern.Pattern, propertyExpression);

                prerequisites.AddRange(propertyMatch.Prerequisites);
                conditions.Add(propertyMatch.Condition);
                bindings.AddRange(propertyMatch.Bindings);
            }
        }

        if (pattern.PositionalPatternClause is { } positionalClause)
        {
            for (var index = 0; index < positionalClause.Subpatterns.Count; index++)
            {
                var subpattern = positionalClause.Subpatterns[index];
                var elementExpression = CreateTupleElementAccessExpression(comparandExpression, index + 1);
                var elementMatch = Build(context, subpattern.Pattern, elementExpression);

                prerequisites.AddRange(elementMatch.Prerequisites);
                conditions.Add(elementMatch.Condition);
                bindings.AddRange(elementMatch.Bindings);
            }
        }

        if (pattern.Designation is not null)
        {
            var designationType = resolvedType ?? context.Semantics.GetTypeInfo(pattern).ConvertedType;
            var capture = TryBuildCaptureAssignment(context, pattern.Designation, comparandExpression, designationType, annotate: resolvedType is not null);
            if (capture is not null)
            {
                bindings.Add(capture);
            }
        }

        var combinedCondition = CombineConditions(conditions);
        return new PatternMatchResult(combinedCondition, prerequisites, bindings);
    }

    private static Expression CombineConditions(IReadOnlyList<Expression> conditions)
    {
        if (conditions.Count == 0)
        {
            return new BooleanExpression { Value = true };
        }

        var current = conditions[0];
        for (var i = 1; i < conditions.Count; i++)
        {
            current = new BinaryOperatorExpression
            {
                Left = current,
                Op = BinOp.And,
                Right = conditions[i],
            };
        }

        return current;
    }

    private static Expression BuildTypeCheck(
        TranspilationContext context,
        Expression comparandExpression,
        ITypeSymbol typeSymbol)
    {
        var mappedType = StandardUtility.GetMappedType(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Expression typeExpression = StringExpression.FromString(mappedType);

        return FunctionCallAst.Basic(
            "CS.is",
            CloneExpression(comparandExpression),
            typeExpression);
    }

    private static Expression CreateLengthExpression(Expression comparandExpression) =>
        new UnaryOperatorExpression
        {
            Op = UnOp.Length,
            Operand = CloneExpression(comparandExpression),
        };

    private static Expression CloneExpression(Expression expression) => (Expression)expression.DeepClone();

    private static LocalAssignment? TryBuildSliceCaptureAssignment(
        TranspilationContext context,
        PatternSyntax slicePattern,
        Expression comparandExpression,
        int headCount,
        int tailCount)
    {
        var captureInfo = TryGetSliceCaptureInfo(context, slicePattern);
        if (captureInfo is null)
        {
            return null;
        }
        var (name, annotations) = captureInfo;
        context.RegisterListSliceVariable(name);

        var startExpression = NumberExpression.From(headCount + 1);
        var lengthExpression = BuildSliceLengthExpression(comparandExpression, tailCount);

        var sliceExpression = FunctionCallAst.Basic(
            "CS.List.slice",
            CloneExpression(comparandExpression),
            startExpression,
            lengthExpression);

        return new LocalAssignment
        {
            Names = [SymbolExpression.FromString(name)],
            Expressions = [sliceExpression],
            Types = annotations,
        };
    }

    private static Expression BuildSliceLengthExpression(
        Expression comparandExpression,
        int tailCount)
    {
        Expression expression = CreateLengthExpression(comparandExpression);

        if (tailCount > 0)
        {
            expression = new BinaryOperatorExpression
            {
                Left = expression,
                Op = BinOp.Minus,
                Right = NumberExpression.From(tailCount),
            };
        }

        return expression;
    }

    private static string? GetPropertySubpatternName(SubpatternSyntax subpattern)
    {
        return subpattern.NameColon?.Name switch
        {
            SimpleNameSyntax simpleName => simpleName.Identifier.ValueText,
            _ => null,
        };
    }

    private static Expression CreatePropertyAccessExpression(Expression comparandExpression, string propertyName)
    {
        var renderedComparand = RenderExpression(comparandExpression);
        return SymbolExpression.FromString($"{renderedComparand}.{propertyName}");
    }

    private static Expression CreateTupleElementAccessExpression(Expression comparandExpression, int position)
    {
        var renderedComparand = RenderExpression(comparandExpression);
        return SymbolExpression.FromString($"{renderedComparand}.Item{position}");
    }

    private static string RenderExpression(Expression expression)
    {
        var renderer = new RendererWalker();
        var chunk = new Chunk { Block = Block.Empty() };
        chunk.Block.AddStatement(Return.FromExpressions([(Expression)expression.DeepClone()]));

        var rendered = renderer.Render(chunk);
        var firstLine = rendered.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        const string prefix = "return ";
        return firstLine.StartsWith(prefix, StringComparison.Ordinal) ? firstLine[prefix.Length..] : firstLine;
    }

    private static SliceCaptureInfo? TryGetSliceCaptureInfo(TranspilationContext context, PatternSyntax pattern)
    {
        return pattern switch
        {
            VarPatternSyntax varPattern => CreateCaptureFromDesignation(
                context,
                varPattern.Designation,
                context.Semantics.GetTypeInfo(varPattern).ConvertedType,
                annotate: false),
            DeclarationPatternSyntax declarationPattern => CreateCaptureFromDesignation(
                context,
                declarationPattern.Designation,
                context.Semantics.GetTypeInfo(declarationPattern.Type).Type,
                annotate: true),
            RecursivePatternSyntax recursivePattern when recursivePattern.Designation is not null =>
                CreateCaptureFromDesignation(
                    context,
                    recursivePattern.Designation,
                    recursivePattern.Type is not null
                        ? context.Semantics.GetTypeInfo(recursivePattern.Type).Type
                        : context.Semantics.GetTypeInfo(recursivePattern).ConvertedType,
                    annotate: recursivePattern.Type is not null),
            _ => null,
        };
    }

    private static SliceCaptureInfo? CreateCaptureFromDesignation(
        TranspilationContext context,
        VariableDesignationSyntax? designation,
        ITypeSymbol? typeSymbol,
        bool annotate)
    {
        if (designation is null)
        {
            return null;
        }

        switch (designation)
        {
            case SingleVariableDesignationSyntax single:
            {
                var name = single.Identifier.ValueText;
                if (string.IsNullOrEmpty(name) || name == "_")
                {
                    return null;
                }

                var typeAnnotations = annotate && typeSymbol is not null
                    ? new List<AstTypeInfo> { SyntaxUtilities.TypeInfoFromSymbol(typeSymbol) }
                    : new List<AstTypeInfo>();

                return new SliceCaptureInfo(name, typeAnnotations);
            }

            case ParenthesizedVariableDesignationSyntax parenthesized when parenthesized.Variables.Count == 1:
                return CreateCaptureFromDesignation(context, parenthesized.Variables[0], typeSymbol, annotate);

            default:
                throw Logger.UnsupportedError(designation, $"unsupported slice capture designation '{designation.Kind()}'");
        }
    }

    private sealed record SliceCaptureInfo(string Name, List<AstTypeInfo> TypeAnnotations);

    private static LocalAssignment? TryBuildCaptureAssignment(
        TranspilationContext context,
        VariableDesignationSyntax? designation,
        Expression comparandExpression,
        ITypeSymbol? typeSymbol,
        bool annotate)
    {
        var captureInfo = CreateCaptureFromDesignation(context, designation, typeSymbol, annotate);
        if (captureInfo is null)
        {
            return null;
        }
        var (name, annotations) = captureInfo;

        return new LocalAssignment
        {
            Names = [SymbolExpression.FromString(name)],
            Expressions = [CloneExpression(comparandExpression)],
            Types = annotations,
        };
    }

    private static PatternMatchResult BuildVarPattern(
        TranspilationContext context,
        VarPatternSyntax pattern,
        Expression comparandExpression)
    {
        var bindings = new List<Statement>();
        var captureAssignment = TryBuildCaptureAssignment(
            context,
            pattern.Designation,
            comparandExpression,
            context.Semantics.GetTypeInfo(pattern).ConvertedType,
            annotate: false);

        if (captureAssignment is not null)
        {
            bindings.Add(captureAssignment);
        }

        return new PatternMatchResult(new BooleanExpression { Value = true }, Array.Empty<Statement>(), bindings);
    }

    private static bool IsSupportedListType(ITypeSymbol? typeSymbol, TranspilationContext context)
    {
        if (typeSymbol is null)
        {
            return false;
        }

        if (typeSymbol is IArrayTypeSymbol)
        {
            return true;
        }

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            if (IsListCandidate(namedType, context))
            {
                return true;
            }

            foreach (var interfaceType in namedType.AllInterfaces)
            {
                if (interfaceType is INamedTypeSymbol interfaceNamed && IsListCandidate(interfaceNamed, context))
                {
                    return true;
                }
            }

            if (namedType.BaseType is { } baseType && IsSupportedListType(baseType, context))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsListCandidate(INamedTypeSymbol symbol, TranspilationContext context)
    {
        var compilation = context.Compilation;
        return Matches(symbol, compilation.GetTypeByMetadataName("System.Collections.Generic.IList`1"))
               || Matches(symbol, compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyList`1"))
               || Matches(symbol, compilation.GetTypeByMetadataName("System.Collections.Generic.List`1"))
               || Matches(symbol, compilation.GetTypeByMetadataName("System.Collections.IList"));
    }

    private static bool Matches(INamedTypeSymbol symbol, INamedTypeSymbol? expected)
    {
        if (expected is null)
        {
            return false;
        }

        if (symbol.IsGenericType && expected.IsGenericType)
        {
            return SymbolEqualityComparer.Default.Equals(symbol.ConstructedFrom, expected);
        }

        return SymbolEqualityComparer.Default.Equals(symbol, expected);
    }
}

internal readonly record struct PatternMatchResult(
    Expression Condition,
    IReadOnlyList<Statement> Prerequisites,
    IReadOnlyList<Statement> Bindings);
