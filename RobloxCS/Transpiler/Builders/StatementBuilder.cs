using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Functions;
using RobloxCS.AST.Statements;
using RobloxCS.AST;
using RobloxCS.AST.Parameters;
using RobloxCS.AST.Types;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2.Nodes.Expressions;
using RobloxCS.TranspilerV2.Nodes;
using RobloxCS.TranspilerV2.Nodes.Statements;

namespace RobloxCS.TranspilerV2.Builders;

using FunctionCallAst = RobloxCS.AST.Expressions.FunctionCall;
using AstTypeInfo = RobloxCS.AST.Types.TypeInfo;

public class StatementBuilder {
    public static Statement Transpile(StatementSyntax stmt, TranspilationContext ctx) {
        if (TransformerRegistry.TryGetStatementTransformer(stmt.Kind(), out var transformer) && transformer is not null) {
            return transformer(ctx, stmt);
        }

        return stmt switch {
            ExpressionStatementSyntax exprStmtSyntax => BuildFromExprStmt(exprStmtSyntax, ctx),
            LocalDeclarationStatementSyntax localDeclStmtSyntax => BuildFromLocalDeclStmt(localDeclStmtSyntax, ctx),
            ReturnStatementSyntax returnStmtSyntax => BuildFromReturnStmt(returnStmtSyntax, ctx),
            IfStatementSyntax ifStmtSyntax => BuildFromIfStmt(ifStmtSyntax, ctx),
            WhileStatementSyntax whileStmtSyntax => BuildFromWhileStmt(whileStmtSyntax, ctx),
            ForStatementSyntax forStmtSyntax => BuildFromNumericForStmt(forStmtSyntax, ctx),
            DoStatementSyntax doStmtSyntax => BuildFromDoStmt(doStmtSyntax, ctx),
            LocalFunctionStatementSyntax localFunctionSyntax => BuildFromLocalFunctionStmt(localFunctionSyntax, ctx),
            BreakStatementSyntax breakStmtSyntax => BuildFromBreakStmt(breakStmtSyntax, ctx),
            ContinueStatementSyntax continueStmtSyntax => BuildFromContinueStmt(continueStmtSyntax, ctx),
            ThrowStatementSyntax throwStmtSyntax => BuildFromThrowStmt(throwStmtSyntax, ctx),
            YieldStatementSyntax yieldStatementSyntax => YieldStatementTransformer.Transform(ctx, yieldStatementSyntax),

            _ => throw new NotSupportedException($"Unsupported statement: {stmt.Kind()}"),
        };
    }

    private static Statement BuildFromLocalDeclStmt(LocalDeclarationStatementSyntax localDeclStmtSyntax, TranspilationContext ctx) {
        var decl = localDeclStmtSyntax.Declaration;
        var vars = decl.Variables;

        var varNames = vars.Select(vds => vds.Identifier.ValueText).ToList();
        var initExprSyntaxes = vars.Where(v => v.Initializer is not null).Select(v => v.Initializer!.Value);
        var initExprs = initExprSyntaxes.Select(s => ExpressionBuilder.BuildFromSyntax(s, ctx)).ToList();

        var typeInfos = new List<AstTypeInfo>(vars.Count);
        var annotateTypes = false;
        foreach (var variable in vars) {
            if (ctx.Semantics.GetDeclaredSymbol(variable) is ILocalSymbol localSymbol) {
                var typeInfo = SyntaxUtilities.TypeInfoFromSymbol(localSymbol.Type);
                typeInfos.Add(typeInfo);
                if (!decl.Type.IsVar && typeInfo is not BasicTypeInfo) {
                    annotateTypes = true;
                }
            } else {
                typeInfos.Add(BasicTypeInfo.FromString("any"));
            }
        }

        return new LocalAssignment {
            Names = varNames.Select(SymbolExpression.FromString).ToList(),
            Expressions = initExprs,
            Types = annotateTypes ? typeInfos : [],
        };
    }

    private static Statement BuildFromExprStmt(ExpressionStatementSyntax exprStmt, TranspilationContext ctx) {
        if (exprStmt.Expression is ConditionalAccessExpressionSyntax conditionalAccess
            && conditionalAccess.WhenNotNull is InvocationExpressionSyntax invocationExpression)
        {
            return LowerOptionalInvocation(conditionalAccess, invocationExpression, ctx);
        }

        var expr = exprStmt.Expression;

        switch (expr) {
            case AssignmentExpressionSyntax assignExpr:
                return AssignmentExpressionTransformer.BuildStatement(ctx, assignExpr);

            case InvocationExpressionSyntax invocationSyntax: {
                var call = ExpressionBuilder.BuildFunctionCall(invocationSyntax, ctx);

                return new FunctionCallStatement {
                    Prefix = call.Prefix,
                    Suffixes = call.Suffixes,
                };
            }

            case AwaitExpressionSyntax awaitExpression:
            {
                if (ExpressionBuilder.BuildFromSyntax(awaitExpression, ctx) is FunctionCallAst awaitCall)
                {
                    return new FunctionCallStatement
                    {
                        Prefix = awaitCall.Prefix,
                        Suffixes = awaitCall.Suffixes,
                    };
                }

                throw new NotSupportedException("Unsupported await expression.");
            }
        }

        throw new Exception($"Unhandled expression {expr.Kind()}");
    }

    private static Statement LowerOptionalInvocation(ConditionalAccessExpressionSyntax conditionalAccess, InvocationExpressionSyntax invocation, TranspilationContext ctx)
    {
        var tempName = ctx.AllocateTempName("optional", "target");
        var tempSymbol = SymbolExpression.FromString(tempName);

        var baseExpression = ExpressionBuilder.BuildFromSyntax(conditionalAccess.Expression, ctx);

        var assignTemp = new LocalAssignment
        {
            Names = [tempSymbol],
            Expressions = [baseExpression],
            Types = [],
        };

        var methodSymbol = ctx.Semantics.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var arguments = invocation.ArgumentList.Arguments
            .Select(argument => ExpressionBuilder.BuildFromSyntax(argument.Expression, ctx))
            .ToArray();

        string callName;
        if (methodSymbol is IMethodSymbol { MethodKind: MethodKind.ReducedExtension, ReducedFrom: { } originalExtension })
        {
            callName = $"{tempName}:{originalExtension.Name}";
        }
        else if (methodSymbol is { IsStatic: true, ContainingType: { } containingType })
        {
            ctx.AddDependency(containingType);
            callName = $"{ctx.GetTypeName(containingType)}.{methodSymbol.Name}";
        }
        else
        {
            var methodName = methodSymbol?.Name
                ?? (invocation.Expression as MemberBindingExpressionSyntax)?.Name.Identifier.ValueText
                ?? "Invoke";
            callName = $"{tempName}:{methodName}";
        }

        var callExpression = FunctionCallAst.Basic(callName, arguments);
        var callStatement = new FunctionCallStatement
        {
            Prefix = callExpression.Prefix,
            Suffixes = callExpression.Suffixes,
        };

        var condition = new BinaryOperatorExpression
        {
            Left = tempSymbol,
            Op = BinOp.TildeEqual,
            Right = SymbolExpression.FromString("nil"),
        };

        var ifStatement = new If
        {
            Condition = condition,
            ThenBody = WrapInBlock(callStatement),
        };

        var block = Block.Empty();
        block.AddStatement(assignTemp);
        block.AddStatement(ifStatement);
        return DoStatement.FromBlock(block);
    }

    private static Statement BuildFromReturnStmt(ReturnStatementSyntax returnStmt, TranspilationContext ctx) {
        ctx.MarkReturnEncountered();

        if (!ctx.IsInsideTryScope) {
            if (returnStmt.Expression is null) {
                return Return.Empty();
            }

            if (TryBuildTupleReturnExpressions(returnStmt.Expression, ctx, out var tupleExpressions))
            {
                if (tupleExpressions.Count == 0)
                {
                    return Return.Empty();
                }

                return Return.FromExpressions(tupleExpressions);
            }

            var expression = ExpressionBuilder.BuildFromSyntax(returnStmt.Expression, ctx);
            return Return.FromExpressions([expression]);
        }

        Expression payload;
        if (returnStmt.Expression is null) {
            payload = FunctionCallAst.Basic("table.pack");
        } else if (TryBuildTupleReturnExpressions(returnStmt.Expression, ctx, out var tupleExpressions))
        {
            payload = FunctionCallAst.Basic("table.pack", tupleExpressions.ToArray());
        } else {
            var expression = ExpressionBuilder.BuildFromSyntax(returnStmt.Expression, ctx);
            payload = FunctionCallAst.Basic("table.pack", expression);
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
            expressions.Add(ExpressionBuilder.BuildFromSyntax(argument.Expression, ctx));
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

    private static Statement BuildFromIfStmt(IfStatementSyntax ifStmt, TranspilationContext ctx) {
        var condition = ExpressionBuilder.BuildFromSyntax(ifStmt.Condition, ctx);
        var thenBlock = BuildNestedBlock(ifStmt.Statement, ctx);

        Block? elseBlock = null;
        if (ifStmt.Else is { Statement: { } elseSyntax }) {
            elseBlock = BuildNestedBlock(elseSyntax, ctx);
        }

        return new If {
            Condition = condition,
            ThenBody = thenBlock,
            ElseBody = elseBlock,
        };
    }

    private static Block BuildNestedBlock(StatementSyntax syntax, TranspilationContext ctx) {
        if (syntax is BlockSyntax blockSyntax)
        {
            return BuildBlockStatement(blockSyntax, ctx);
        }

        var statement = Transpile(syntax, ctx);
        var block = Block.Empty();
        ctx.AppendPrerequisites(block);
        block.AddStatement(statement);
        return block;
    }

    private static Block BuildBlockStatement(BlockSyntax blockSyntax, TranspilationContext ctx) {
        ctx.PushScope();
        var block = Block.Empty();

        foreach (var statementSyntax in blockSyntax.Statements) {
            var statement = Transpile(statementSyntax, ctx);
            ctx.AppendPrerequisites(block);
            block.AddStatement(statement);
        }

        ctx.AppendPrerequisites(block);
        ctx.PopScope();

        return block;
    }

    private static Block WrapInBlock(Statement statement) {
        var block = Block.Empty();
        block.AddStatement(statement);
        return block;
    }

    private static Statement BuildFromWhileStmt(WhileStatementSyntax whileStmt, TranspilationContext ctx) {
        var condition = ExpressionBuilder.BuildFromSyntax(whileStmt.Condition, ctx);

        ctx.LoopStack.Push(new LoopInfo());
        var body = BuildNestedBlock(whileStmt.Statement, ctx);
        ctx.LoopStack.Pop();

        return new While {
            Condition = condition,
            Body = body,
        };
    }

    private static Statement BuildFromNumericForStmt(ForStatementSyntax forStmt, TranspilationContext ctx) {
        if (!IsSimpleNumericFor(forStmt, ctx, out var identifier, out var startExpr, out var limitExpr, out var stepExpr)) {
            throw new NotSupportedException("Only simple numeric for loops are supported at this time.");
        }

        ctx.PushScope();
        ctx.LoopStack.Push(new LoopInfo());
        var bodyBlock = BuildNestedBlock(forStmt.Statement, ctx);
        ctx.LoopStack.Pop();
        ctx.PopScope();

        return new NumericFor {
            Name = VarName.FromString(identifier),
            Start = startExpr,
            End = limitExpr,
            Step = stepExpr,
            Body = bodyBlock,
        };
    }

    private static bool IsSimpleNumericFor(
        ForStatementSyntax forStmt,
        TranspilationContext ctx,
        out string identifier,
        out Expression startExpr,
        out Expression limitExpr,
        out Expression stepExpr
    ) {
        identifier = string.Empty;
        startExpr = limitExpr = stepExpr = SymbolExpression.FromString("0");

        if (forStmt.Declaration is null || forStmt.Declaration.Variables.Count != 1) {
            return false;
        }

        if (forStmt.Initializers.Count > 0 || forStmt.Condition is null || forStmt.Incrementors.Count != 1) {
            return false;
        }

        var variable = forStmt.Declaration.Variables[0];
        if (variable.Initializer?.Value is not ExpressionSyntax initValue) {
            return false;
        }

        identifier = variable.Identifier.ValueText;
        startExpr = ExpressionBuilder.BuildFromSyntax(initValue, ctx);

        if (forStmt.Condition is not BinaryExpressionSyntax condition
         || condition.Kind() is not (SyntaxKind.LessThanOrEqualExpression or SyntaxKind.LessThanExpression)
         || condition.Left is not IdentifierNameSyntax conditionIdentifier
         || conditionIdentifier.Identifier.ValueText != identifier) {
            return false;
        }

        limitExpr = ExpressionBuilder.BuildFromSyntax(condition.Right, ctx);

        if (forStmt.Incrementors[0] is PostfixUnaryExpressionSyntax incrementUnary) {
            if (incrementUnary.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PreIncrementExpression
             && incrementUnary.Operand is IdentifierNameSyntax { Identifier.ValueText: var incName } && incName == identifier) {
                stepExpr = NumberExpression.From(1);
                return true;
            }
        }

        if (forStmt.Incrementors[0] is PrefixUnaryExpressionSyntax prefixUnary) {
            if (prefixUnary.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PostIncrementExpression
             && prefixUnary.Operand is IdentifierNameSyntax { Identifier.ValueText: var incName2 } && incName2 == identifier) {
                stepExpr = NumberExpression.From(1);
                return true;
            }
        }

        if (forStmt.Incrementors[0] is AssignmentExpressionSyntax incrementAssign
         && incrementAssign.Kind() == SyntaxKind.AddAssignmentExpression
         && incrementAssign.Left is IdentifierNameSyntax { Identifier.ValueText: var varName } && varName == identifier) {
            stepExpr = ExpressionBuilder.BuildFromSyntax(incrementAssign.Right, ctx);
            return true;
        }

        return false;
    }

    private static Statement BuildFromDoStmt(DoStatementSyntax doStmt, TranspilationContext ctx) {
        var condition = ExpressionBuilder.BuildFromSyntax(doStmt.Condition, ctx);
        var body = BuildNestedBlock(doStmt.Statement, ctx);

        return new Repeat {
            Body = body,
            Condition = new UnaryOperatorExpression {
                Op = UnOp.Not,
                Operand = condition,
            },
        };
    }

    private static Statement BuildFromThrowStmt(ThrowStatementSyntax throwStmt, TranspilationContext ctx)
    {
        var expressionSyntax = throwStmt.Expression;

        Expression messageExpression;

        if (expressionSyntax is ObjectCreationExpressionSyntax objectCreation)
        {
            if (objectCreation.ArgumentList is { Arguments.Count: > 0 } arguments)
            {
                messageExpression = ExpressionBuilder.BuildFromSyntax(arguments.Arguments[0].Expression, ctx);
            }
            else
            {
                messageExpression = StringExpression.FromString("Unhandled exception");
            }
        }
        else if (expressionSyntax is null)
        {
            messageExpression = StringExpression.FromString("Unhandled error");
        }
        else
        {
            messageExpression = ExpressionBuilder.BuildFromSyntax(expressionSyntax, ctx);
        }

        var errorCall = FunctionCallAst.Basic("error", messageExpression);
        return new FunctionCallStatement
        {
            Prefix = errorCall.Prefix,
            Suffixes = errorCall.Suffixes,
        };
    }

    private static Statement BuildFromLocalFunctionStmt(LocalFunctionStatementSyntax localFunctionSyntax, TranspilationContext ctx) {
        if (ctx.Semantics.GetDeclaredSymbol(localFunctionSyntax) is not IMethodSymbol methodSymbol) {
            throw new InvalidOperationException("Failed to resolve local function symbol.");
        }

        if (methodSymbol.IsAsync) {
            ctx.MarkAsync(methodSymbol);
        }

        var functionBody = FunctionBuilder.CreateAnonymousFunctionBody(
            methodSymbol,
            localFunctionSyntax.Body,
            localFunctionSyntax.ExpressionBody,
            ctx
        );

        Expression functionExpression = new AnonymousFunction { Body = functionBody };

        if (methodSymbol.IsAsync) {
            functionExpression = FunctionCallAst.Basic("CS.async", functionExpression);
        }

        return new LocalAssignment {
            Names = [SymbolExpression.FromString(methodSymbol.Name)],
            Expressions = [functionExpression],
            Types = [],
        };
    }

    private static Statement BuildFromBreakStmt(BreakStatementSyntax breakStmt, TranspilationContext ctx) {
        if (ctx.IsInsideTryScope) {
            ctx.MarkBreakEncountered();
            return Return.FromExpressions([
                SymbolExpression.FromString("CS.TRY_BREAK"),
                FunctionCallAst.Basic("table.pack"),
            ]);
        }

        return new Break();
    }

    private static Statement BuildFromContinueStmt(ContinueStatementSyntax continueStmt, TranspilationContext ctx) {
        if (ctx.IsInsideTryScope) {
            ctx.MarkContinueEncountered();
            return Return.FromExpressions([
                SymbolExpression.FromString("CS.TRY_CONTINUE"),
                FunctionCallAst.Basic("table.pack"),
            ]);
        }

        return new Continue();
    }

}
