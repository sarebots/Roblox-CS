using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Statements;

namespace RobloxCS.TranspilerV2.Builders;

internal sealed record ObjectInitializerResult(string BindingName, List<Statement> Statements);

internal static class ObjectInitializerBuilder {
    private const string BindingBaseName = "_binding";

    public static bool TryBuild(ExpressionSyntax expression, ITypeSymbol? fallbackType, TranspilationContext ctx, out ObjectInitializerResult result) {
        result = default!;

        switch (expression) {
            case ObjectCreationExpressionSyntax objectCreation when CanLower(objectCreation, ctx): {
                var bindingName = ctx.AllocateTempName(BindingBaseName);
                var creationExpression = ExpressionBuilder.BuildObjectCreationWithoutInitializer(objectCreation, ctx);
                result = Build(bindingName, creationExpression, objectCreation.Initializer!, ctx);
                return true;
            }

            case ImplicitObjectCreationExpressionSyntax implicitCreation when CanLower(implicitCreation, ctx): {
                var bindingName = ctx.AllocateTempName(BindingBaseName);
                var typeInfo = ctx.Semantics.GetTypeInfo(implicitCreation);
                var typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType ?? fallbackType;
                var creationExpression = ExpressionBuilder.BuildImplicitObjectCreationWithoutInitializer(implicitCreation, typeSymbol, ctx);
                result = Build(bindingName, creationExpression, implicitCreation.Initializer!, ctx);
                return true;
            }

            default:
                return false;
        }
    }

    public static ObjectInitializerResult BuildForExpression(ObjectCreationExpressionSyntax syntax, TranspilationContext ctx) {
        if (!CanLower(syntax, ctx)) {
            throw new NotSupportedException("Object initializers are not supported yet.");
        }

        var bindingName = ctx.AllocateTempName(BindingBaseName);
        var creationExpression = ExpressionBuilder.BuildObjectCreationWithoutInitializer(syntax, ctx);
        return Build(bindingName, creationExpression, syntax.Initializer!, ctx);
    }

    public static ObjectInitializerResult BuildForExpression(
        ImplicitObjectCreationExpressionSyntax syntax,
        TranspilationContext ctx,
        ITypeSymbol? fallbackType
    ) {
        if (!CanLower(syntax, ctx)) {
            throw new NotSupportedException("Object initializers are not supported yet.");
        }

        var bindingName = ctx.AllocateTempName(BindingBaseName);
        var typeInfo = ctx.Semantics.GetTypeInfo(syntax);
        var typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType ?? fallbackType;
        var creationExpression = ExpressionBuilder.BuildImplicitObjectCreationWithoutInitializer(syntax, typeSymbol, ctx);
        return Build(bindingName, creationExpression, syntax.Initializer!, ctx);
    }

    private static ObjectInitializerResult Build(
        string bindingName,
        Expression creationExpression,
        InitializerExpressionSyntax initializer,
        TranspilationContext ctx
    ) {
        var statements = new List<Statement> {
            new LocalAssignment {
                Names = [SymbolExpression.FromString(bindingName)],
                Expressions = [creationExpression],
                Types = [],
            },
        };

        foreach (var expression in initializer.Expressions) {
            if (expression is not AssignmentExpressionSyntax assignmentExpression) {
                throw new NotSupportedException($"Unsupported object initializer expression: {expression.Kind()}.");
            }

            var accessor = BuildAccessorPath(assignmentExpression.Left);
            var targetPath = CombinePath(bindingName, accessor);
            var right = assignmentExpression.Right;

            switch (right) {
                case ObjectCreationExpressionSyntax objectCreation when CanLower(objectCreation, ctx): {
                    var nestedCreation = ExpressionBuilder.BuildObjectCreationWithoutInitializer(objectCreation, ctx);
                    var nestedBindingName = ctx.AllocateTempName(BindingBaseName);
                    var nestedResult = Build(nestedBindingName, nestedCreation, objectCreation.Initializer!, ctx);
                    statements.AddRange(nestedResult.Statements);
                    statements.Add(CreateAssignment(targetPath, SymbolExpression.FromString(nestedBindingName)));
                    break;
                }

                case ImplicitObjectCreationExpressionSyntax implicitCreation when CanLower(implicitCreation, ctx): {
                    var nestedTypeInfo = ctx.Semantics.GetTypeInfo(implicitCreation);
                    var nestedType = nestedTypeInfo.Type ?? nestedTypeInfo.ConvertedType;
                    var nestedCreation = ExpressionBuilder.BuildImplicitObjectCreationWithoutInitializer(implicitCreation, nestedType, ctx);
                    var nestedBindingName = ctx.AllocateTempName(BindingBaseName);
                    var nestedResult = Build(nestedBindingName, nestedCreation, implicitCreation.Initializer!, ctx);
                    statements.AddRange(nestedResult.Statements);
                    statements.Add(CreateAssignment(targetPath, SymbolExpression.FromString(nestedBindingName)));
                    break;
                }

                default: {
                    var valueExpression = ExpressionBuilder.BuildFromSyntax(right, ctx);
                    statements.Add(CreateAssignment(targetPath, valueExpression));
                    break;
                }
            }
        }

        return new ObjectInitializerResult(bindingName, statements);
    }

    private static Assignment CreateAssignment(string targetPath, Expression value) {
        return new Assignment {
            Vars = [VarName.FromString(targetPath)],
            Expressions = [value],
        };
    }

    private static string CombinePath(string bindingName, string accessor) {
        return string.IsNullOrEmpty(accessor) ? bindingName : $"{bindingName}.{accessor}";
    }

    private static string BuildAccessorPath(ExpressionSyntax expression) {
        return expression switch {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => string.Join('.', FlattenMemberAccess(memberAccess)),
            _ => expression.ToString(),
        };
    }

    private static IEnumerable<string> FlattenMemberAccess(MemberAccessExpressionSyntax memberAccess) {
        var stack = new Stack<string>();
        ExpressionSyntax? current = memberAccess;

        while (current is MemberAccessExpressionSyntax access) {
            stack.Push(access.Name.Identifier.ValueText);
            current = access.Expression;
        }

        if (current is IdentifierNameSyntax identifier) {
            stack.Push(identifier.Identifier.ValueText);
        }

        return stack;
    }

    public static bool CanLower(ObjectCreationExpressionSyntax syntax, TranspilationContext ctx) {
        if (syntax.Initializer is null) {
            return false;
        }

        var typeInfo = ctx.Semantics.GetTypeInfo(syntax);
        return !IsDictionaryType(typeInfo.Type);
    }

    public static bool CanLower(ImplicitObjectCreationExpressionSyntax syntax, TranspilationContext ctx) {
        if (syntax.Initializer is null) {
            return false;
        }

        var typeInfo = ctx.Semantics.GetTypeInfo(syntax);
        return !IsDictionaryType(typeInfo.Type);
    }

    private static bool IsDictionaryType(ITypeSymbol? symbol) {
        if (symbol is null) {
            return false;
        }

        if (symbol.Name.StartsWith("Dictionary", StringComparison.Ordinal)) {
            return true;
        }

        return symbol.AllInterfaces.Any(interfaceSymbol =>
            interfaceSymbol.Name is "IDictionary" or "IReadOnlyDictionary"
            || (interfaceSymbol.ContainingNamespace?.ToDisplayString() == "System.Collections"
                && interfaceSymbol.Name == "IDictionary"));
    }
}
