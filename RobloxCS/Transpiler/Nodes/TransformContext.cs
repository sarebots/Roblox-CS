using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Expressions;
using RobloxCS.TranspilerV2;

namespace RobloxCS.TranspilerV2.Nodes;

internal sealed class TransformContext
{
    private readonly Func<ExpressionSyntax, Expression> _expressionBuilder;
    private readonly Func<ExpressionSyntax, Expression> _fallbackBuilder;

    public TransformContext(
        TranspilationContext transpilationContext,
        Func<ExpressionSyntax, Expression> expressionBuilder,
        Func<ExpressionSyntax, Expression> fallbackBuilder)
    {
        TranspilationContext = transpilationContext;
        _expressionBuilder = expressionBuilder;
        _fallbackBuilder = fallbackBuilder;
    }

    public TranspilationContext TranspilationContext { get; }
    public SemanticModel SemanticModel => TranspilationContext.Semantics;
    public CSharpCompilation Compilation => TranspilationContext.Compilation;

    public Expression BuildExpression(ExpressionSyntax syntax) => _expressionBuilder(syntax);
    public Expression BuildExpressionWithoutTransformers(ExpressionSyntax syntax) => _fallbackBuilder(syntax);

    public void MarkAsync(ISymbol symbol) => TranspilationContext.MarkAsync(symbol);
    public bool IsAsync(ISymbol symbol) => TranspilationContext.IsAsync(symbol);
    public bool IsInsideLoop => TranspilationContext.IsInsideLoop;
}
