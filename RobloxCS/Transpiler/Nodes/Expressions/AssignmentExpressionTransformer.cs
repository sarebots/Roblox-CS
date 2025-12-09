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
using RobloxCS.AST.Types;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2.Builders;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

using FunctionCallAst = RobloxCS.AST.Expressions.FunctionCall;
using AstTypeInfo = RobloxCS.AST.Types.TypeInfo;

internal static class AssignmentExpressionTransformer
{
    private static readonly SyntaxKind[] SupportedKinds =
    {
        SyntaxKind.SimpleAssignmentExpression,
        SyntaxKind.AddAssignmentExpression,
        SyntaxKind.SubtractAssignmentExpression,
        SyntaxKind.MultiplyAssignmentExpression,
        SyntaxKind.DivideAssignmentExpression,
        SyntaxKind.ModuloAssignmentExpression,
        SyntaxKind.LeftShiftAssignmentExpression,
        SyntaxKind.RightShiftAssignmentExpression,
        SyntaxKind.AndAssignmentExpression,
        SyntaxKind.ExclusiveOrAssignmentExpression,
        SyntaxKind.OrAssignmentExpression,
    };

    public static void Register()
    {
        foreach (var kind in SupportedKinds)
        {
            TransformerRegistry.RegisterExpressionTransformer(kind, Transform);
        }
    }

    private sealed record AssignmentLoweringResult(IReadOnlyList<Statement> Statements, Expression? ValueExpression);

    private static Expression Transform(TransformContext context, ExpressionSyntax syntax)
    {
        if (syntax is not AssignmentExpressionSyntax assignment)
        {
            return context.BuildExpressionWithoutTransformers(syntax);
        }

        if (assignment.Parent is ExpressionStatementSyntax)
        {
            return context.BuildExpressionWithoutTransformers(syntax);
        }

        var result = LowerAssignment(assignment, context.TranspilationContext);

        foreach (var statement in result.Statements)
        {
            context.TranspilationContext.AddPrerequisite(statement);
        }

        return result.ValueExpression ?? context.BuildExpressionWithoutTransformers(assignment.Left);
    }

    public static Statement BuildStatement(TranspilationContext ctx, AssignmentExpressionSyntax assignment)
    {
        var result = LowerAssignment(assignment, ctx);

        if (result.Statements.Count == 1)
        {
            return result.Statements[0];
        }

        var block = Block.Empty();
        foreach (var statement in result.Statements)
        {
            block.AddStatement(statement);
        }

        return DoStatement.FromBlock(block);
    }

    private static AssignmentLoweringResult LowerAssignment(AssignmentExpressionSyntax assignment, TranspilationContext ctx)
    {
        if (TryLowerMacro(assignment, ctx, out var macroResult))
        {
            return macroResult;
        }

        if (assignment.Left is TupleExpressionSyntax tupleExpression)
        {
            return LowerTupleAssignment(tupleExpression, assignment, ctx);
        }

        return LowerScalarAssignment(assignment, ctx);
    }

    private static AssignmentLoweringResult LowerScalarAssignment(AssignmentExpressionSyntax assignment, TranspilationContext ctx)
    {
        var leftVar = VarBuilder.BuildFromExpressionSyntax(assignment.Left, ctx);
        var leftSymbol = assignment.SyntaxTree == ctx.Root.SyntaxTree
            ? ctx.Semantics.GetSymbolInfo(assignment.Left).Symbol
            : null;

        var leftExpression = leftSymbol switch
        {
            IEventSymbol eventSymbol => SymbolExpression.FromString(eventSymbol.IsStatic
                ? $"{ctx.GetTypeName(eventSymbol.ContainingType)}.{eventSymbol.Name}"
                : $"self.{eventSymbol.Name}"),
            _ => ExpressionBuilder.BuildFromSyntax(assignment.Left, ctx),
        };

        var rightExpression = ExpressionBuilder.BuildFromSyntax(assignment.Right, ctx);

        var canUseSemantics = assignment.SyntaxTree == ctx.Root.SyntaxTree;
        var leftRefSymbol = canUseSemantics ? ctx.Semantics.GetSymbolInfo(assignment.Left).Symbol as IParameterSymbol : null;
        var rightSymbol = canUseSemantics ? ctx.Semantics.GetSymbolInfo(assignment.Right).Symbol as IParameterSymbol : null;

        var rightValue = IsRefParameter(rightSymbol)
            ? CreateRefAccessorCall(rightSymbol!.Name)
            : rightExpression;

        if (IsRefParameter(leftRefSymbol))
        {
            return LowerRefAssignment(assignment, leftRefSymbol!, rightValue);
        }

        var statement = new Assignment
        {
            Vars = [leftVar],
            Expressions = [rightValue],
            Operator = assignment.Kind() == SyntaxKind.SimpleAssignmentExpression
                ? "="
                : SyntaxUtilities.CompoundAssignmentOperatorString(assignment.Kind()),
        };

        return new AssignmentLoweringResult([statement], leftExpression);
    }

    private static AssignmentLoweringResult LowerRefAssignment(
        AssignmentExpressionSyntax assignment,
        IParameterSymbol parameterSymbol,
        Expression rightExpression)
    {
        var newValue = assignment.Kind() == SyntaxKind.SimpleAssignmentExpression
            ? rightExpression
            : BuildCompoundRefValue(parameterSymbol.Name, assignment.Kind(), rightExpression);

        var setterCall = FunctionCallAst.Basic(parameterSymbol.Name, newValue);
        var setterStatement = CreateCallStatement(setterCall);
        var accessor = CreateRefAccessorCall(parameterSymbol.Name);

        return new AssignmentLoweringResult([setterStatement], accessor);
    }

    private static Expression BuildCompoundRefValue(string parameterName, SyntaxKind assignmentKind, Expression rightExpression)
    {
        var binOp = SyntaxUtilities.CompoundAssignmentKindToBinOp(assignmentKind);
        return new BinaryOperatorExpression
        {
            Left = CreateRefAccessorCall(parameterName),
            Op = binOp,
            Right = rightExpression,
        };
    }

    private static AssignmentLoweringResult LowerTupleAssignment(
        TupleExpressionSyntax tupleExpression,
        AssignmentExpressionSyntax assignment,
        TranspilationContext ctx)
    {
        var vars = tupleExpression.Arguments
            .Select(argument => VarBuilder.BuildFromExpressionSyntax(argument.Expression, ctx))
            .ToList();

        var valueExpression = ExpressionBuilder.BuildFromSyntax(assignment.Right, ctx);
        var initializerType = ctx.Semantics.GetTypeInfo(assignment.Right).Type;

        if (IsSystemValueTuple(initializerType))
        {
            valueExpression = FunctionCallAst.Basic("CS.unpackTuple", valueExpression);
        }

        var statement = new Assignment
        {
            Vars = vars,
            Expressions = [valueExpression],
        };

        return new AssignmentLoweringResult([statement], valueExpression);
    }

    private static bool TryLowerMacro(
        AssignmentExpressionSyntax assignment,
        TranspilationContext ctx,
        out AssignmentLoweringResult result)
    {
        if (assignment.SyntaxTree != ctx.Root.SyntaxTree)
        {
            result = null!;
            return false;
        }

        var mappedOperator = StandardUtility.GetMappedOperator(assignment.OperatorToken.Text);
        var bit32Method = StandardUtility.GetBit32MethodName(mappedOperator);
        if (bit32Method is not null)
        {
            var leftVar = VarBuilder.BuildFromExpressionSyntax(assignment.Left, ctx);
            var leftExpression = ExpressionBuilder.BuildFromSyntax(assignment.Left, ctx);
            var rightExpression = ExpressionBuilder.BuildFromSyntax(assignment.Right, ctx);

            var call = FunctionCallAst.Basic($"bit32.{bit32Method}", leftExpression, rightExpression);
            var statement = new Assignment
            {
                Vars = [leftVar],
                Expressions = [call],
            };

            result = new AssignmentLoweringResult([statement], leftExpression);
            return true;
        }

        if (assignment.OperatorToken.IsKind(SyntaxKind.PlusEqualsToken) ||
            assignment.OperatorToken.IsKind(SyntaxKind.MinusEqualsToken))
        {
            if (ctx.Semantics.GetSymbolInfo(assignment.Left).Symbol is IEventSymbol eventSymbol
                && ctx.Semantics.GetSymbolInfo(assignment.Right).Symbol is IMethodSymbol methodSymbol)
            {
                result = LowerEventAssignment(assignment, ctx, eventSymbol, methodSymbol);
                return true;
            }
        }

        result = null!;
        return false;
    }

    private static AssignmentLoweringResult LowerEventAssignment(
        AssignmentExpressionSyntax assignment,
        TranspilationContext ctx,
        IEventSymbol eventSymbol,
        IMethodSymbol methodSymbol)
    {
        var connectionName = GetOrCreateConnectionName(eventSymbol, assignment);
        var targetName = !eventSymbol.IsStatic ? $"self.{connectionName}" : connectionName;
        var callback = BuildEventCallbackExpression(methodSymbol, assignment.Right, ctx);
        var eventTarget = ExpressionBuilder.BuildAccessString(assignment.Left, ctx);
        var connectCall = FunctionCallAst.Basic($"{eventTarget}:Connect", callback);

        var connectionSymbol = SymbolExpression.FromString(targetName);

        if (assignment.OperatorToken.IsKind(SyntaxKind.PlusEqualsToken))
        {
            var disconnectsInside = CallbackDisconnectsItself(callback, connectionName);

            Statement statement;
            if (disconnectsInside)
            {
                 statement = BuildScopedConnectionDeclaration(targetName, connectCall);
            }
            else if (!eventSymbol.IsStatic)
            {
                statement = new Assignment
                {
                    Vars = [VarName.FromString(targetName)],
                    Expressions = [connectCall]
                };
            }
            else
            {
                 // Static events: stick to local for now, but really should be module-scoped upvalue.
                 // Assuming existing behavior for static usage.
                 statement = new LocalAssignment
                 {
                    Names = [connectionSymbol],
                    Expressions = [connectCall],
                    Types = [],
                 };
            }

            return new AssignmentLoweringResult([statement], connectionSymbol);
        }

        if (assignment.OperatorToken.IsKind(SyntaxKind.MinusEqualsToken))
        {
            var disconnectCall = FunctionCallAst.Basic($"{targetName}:Disconnect");
            var callStatement = CreateCallStatement(disconnectCall);
            return new AssignmentLoweringResult([callStatement], disconnectCall);
        }

        throw new NotSupportedException($"Unsupported event assignment operator '{assignment.OperatorToken.Text}'.");
    }

    private static Statement BuildScopedConnectionDeclaration(string connectionName, FunctionCallAst connectCall)
    {
        var block = Block.Empty();
        block.AddStatement(new LocalAssignment
        {
            Names = [SymbolExpression.FromString(connectionName)],
            Expressions = [],
            Types = [],
        });
        block.AddStatement(new Assignment
        {
            Vars = [VarName.FromString(connectionName)],
            Expressions = [connectCall],
        });

        return DoStatement.FromBlock(block);
    }

    private static Expression BuildEventCallbackExpression(
        IMethodSymbol methodSymbol,
        ExpressionSyntax rightSyntax,
        TranspilationContext ctx)
    {
        var expression = ExpressionBuilder.BuildFromSyntax(rightSyntax, ctx);

        if (methodSymbol.IsStatic)
        {
            return expression;
        }

        var parameters = methodSymbol.Parameters
            .Where(parameter => !parameter.IsImplicitlyDeclared)
            .ToList();

        var callArguments = parameters
            .Select(parameter => SymbolExpression.FromString(parameter.Name))
            .Cast<Expression>()
            .ToArray();

        var callName = ConvertToMethodCallName(ExpressionBuilder.BuildAccessString(rightSyntax, ctx));
        var call = FunctionCallAst.Basic(callName, callArguments);

        var block = Block.Empty();
        block.AddStatement(Return.FromExpressions([call]));

        var functionBody = new FunctionBody
        {
            Parameters = parameters.Select(parameter => NameParameter.FromString(parameter.Name)).Cast<Parameter>().ToList(),
            TypeSpecifiers = parameters.Select(parameter => SyntaxUtilities.BasicFromSymbol(parameter.Type)).Cast<AstTypeInfo>().ToList(),
            ReturnType = methodSymbol.ReturnsVoid
                ? BasicTypeInfo.Void()
                : SyntaxUtilities.BasicFromSymbol(methodSymbol.ReturnType),
            Body = block,
        };

        return new AnonymousFunction { Body = functionBody };
    }

    private static string ConvertToMethodCallName(string value)
    {
        if (string.IsNullOrEmpty(value) || value.IndexOf(':') >= 0)
        {
            return value;
        }

        var lastDot = value.LastIndexOf('.');
        if (lastDot < 0)
        {
            return value;
        }

        return $"{value[..lastDot]}:{value[(lastDot + 1)..]}";
    }

    private static bool CallbackDisconnectsItself(Expression callback, string connectionName)
    {
        if (callback is not AnonymousFunction anonymousFunction)
        {
            return false;
        }

        return BlockContainsDisconnect(anonymousFunction.Body.Body, connectionName);
    }

    private static bool BlockContainsDisconnect(Block block, string connectionName)
    {
        foreach (var statement in block.Statements)
        {
            switch (statement)
            {
                case FunctionCallStatement callStatement when CallMatchesConnection(callStatement, connectionName):
                    return true;
                case DoStatement doStatement when BlockContainsDisconnect(doStatement.Block, connectionName):
                    return true;
                case If ifStatement when BlockContainsDisconnect(ifStatement.ThenBody, connectionName)
                                          || (ifStatement.ElseBody is not null
                                              && BlockContainsDisconnect(ifStatement.ElseBody, connectionName)):
                    return true;
            }
        }

        return false;
    }

    private static bool CallMatchesConnection(FunctionCallStatement callStatement, string connectionName)
    {
        if (callStatement.Prefix is not NamePrefix namePrefix)
        {
            return false;
        }

        return namePrefix.Name == $"{connectionName}:Disconnect";
    }

    private static string GetOrCreateConnectionName(IEventSymbol eventSymbol, SyntaxNode node)
    {
        var metadata = RobloxCS.Luau.SymbolMetadataManager.Get(eventSymbol);
        metadata.EventConnectionName ??= new RobloxCS.Luau.IdentifierName($"conn_{eventSymbol.Name}");
        return metadata.EventConnectionName.Text;
    }

    private static bool IsRefParameter(IParameterSymbol? parameterSymbol)
    {
        return parameterSymbol is { RefKind: RefKind.Ref or RefKind.Out };
    }

    private static Expression CreateRefAccessorCall(string parameterName)
    {
        return FunctionCallAst.Basic(parameterName);
    }

    private static FunctionCallStatement CreateCallStatement(FunctionCallAst call)
    {
        return new FunctionCallStatement
        {
            Prefix = call.Prefix,
            Suffixes = call.Suffixes,
        };
    }

    private static bool IsSystemValueTuple(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is null)
        {
            return false;
        }

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            if (namedType.IsTupleType)
            {
                return true;
            }

            if (IsSystemNamespace(namedType.ContainingNamespace)
                && (string.Equals(namedType.Name, "ValueTuple", StringComparison.Ordinal)
                    || string.Equals(namedType.Name, "Tuple", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSystemNamespace(INamespaceSymbol? namespaceSymbol)
    {
        if (namespaceSymbol is null)
        {
            return false;
        }

        var displayName = namespaceSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (displayName.StartsWith("global::", StringComparison.Ordinal))
        {
            displayName = displayName["global::".Length..];
        }

        return string.Equals(displayName, "System", StringComparison.Ordinal);
    }
}
