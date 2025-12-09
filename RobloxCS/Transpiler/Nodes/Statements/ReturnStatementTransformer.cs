using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Expressions;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class ReturnStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.ReturnStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax node)
    {
        context.MarkReturnEncountered();
        var returnStatement = (ReturnStatementSyntax)node;

        if (!context.IsInsideTryScope)
        {
            if (returnStatement.Expression is null)
            {
                return Return.Empty();
            }

            if (TryBuildTupleReturnExpressions(returnStatement.Expression, context, out var tupleExpressions))
            {
                if (tupleExpressions.Count == 0)
                {
                    return Return.Empty();
                }

                return Return.FromExpressions(tupleExpressions);
            }

            var expression = Builders.ExpressionBuilder.BuildFromSyntax(returnStatement.Expression, context);
            return Return.FromExpressions([expression]);
        }

        Expression payload;
        if (returnStatement.Expression is null)
        {
            payload = FunctionCall.Basic("table.pack");
        }
        else if (TryBuildTupleReturnExpressions(returnStatement.Expression, context, out var tupleExpressions))
        {
            payload = FunctionCall.Basic("table.pack", tupleExpressions.ToArray());
        }
        else
        {
            var expression = Builders.ExpressionBuilder.BuildFromSyntax(returnStatement.Expression, context);
            payload = FunctionCall.Basic("table.pack", expression);
        }

        return Return.FromExpressions([
            SymbolExpression.FromString("CS.TRY_RETURN"),
            payload,
        ]);
    }

    private static bool TryBuildTupleReturnExpressions(
        ExpressionSyntax expressionSyntax,
        TranspilationContext ctx,
        out List<Expression> expressions)
    {
        expressions = null!;

        if (expressionSyntax is not InvocationExpressionSyntax invocation ||
            !IsGlobalsMacroInvocation(invocation, ctx, "tuple"))
        {
            return false;
        }

        var args = invocation.ArgumentList.Arguments;
        expressions = new List<Expression>(args.Count);
        foreach (var argument in args)
        {
            expressions.Add(Builders.ExpressionBuilder.BuildFromSyntax(argument.Expression, ctx));
        }

        return true;
    }

    private static bool IsGlobalsMacroInvocation(
        InvocationExpressionSyntax invocation,
        TranspilationContext ctx,
        string methodName)
    {
        if (ctx.Semantics.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        var containingType = methodSymbol.ContainingType;
        if (containingType is null)
        {
            return false;
        }

        var namespaceName = containingType.ContainingNamespace?.ToDisplayString();
        return string.Equals(methodSymbol.Name, methodName, StringComparison.Ordinal)
            && string.Equals(containingType.Name, "Globals", StringComparison.Ordinal)
            && string.Equals(namespaceName, "Roblox", StringComparison.Ordinal);
    }
}
