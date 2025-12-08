using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Functions;
using RobloxCS.AST.Parameters;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Types;
using TypeInfo = RobloxCS.AST.Types.TypeInfo;

namespace RobloxCS.TranspilerV2.Builders;

internal static class PropertyBuilder {
    public static IEnumerable<Assignment> CreatePropertyAssignmentsFromProperties(
        IEnumerable<IPropertySymbol> properties,
        TranspilationContext ctx
    ) {
        foreach (var property in properties.Where(prop => !prop.IsStatic)) {
            foreach (var syntaxReference in property.DeclaringSyntaxReferences) {
                if (syntaxReference.GetSyntax() is not PropertyDeclarationSyntax propertySyntax) continue;
                Expression? rhs = null;

                if (propertySyntax.Initializer?.Value is { } initializerExpression)
                {
                    rhs = ExpressionBuilder.BuildFromSyntax(initializerExpression, ctx);
                }
                else
                {
                    rhs = DefaultValueBuilder.CreateDefaultValueExpression(property.Type, ctx);
                }

                if (rhs is null) continue;

                yield return new Assignment {
                    Vars = [VarName.FromString($"self.{property.Name}")],
                    Expressions = [rhs],
                };
            }
        }
    }

    public static bool TryCreateAccessorFunctions(
        INamedTypeSymbol classSymbol,
        IPropertySymbol propertySymbol,
        PropertyDeclarationSyntax propertySyntax,
        TranspilationContext ctx,
        string className,
        out IReadOnlyList<FunctionDeclaration> functions
    ) {
        functions = [];

        if (!SyntaxUtilities.PropertyHasExplicitAccessor(propertySymbol)) {
            return false;
        }

        if (propertySyntax.AccessorList is null) {
            return false;
        }

        var accessorFunctions = new List<FunctionDeclaration>();

        foreach (var accessor in propertySyntax.AccessorList.Accessors) {
            if (accessor.Body is null && accessor.ExpressionBody is null) {
                continue;
            }

            switch (accessor.Kind()) {
                case SyntaxKind.GetAccessorDeclaration:
                    accessorFunctions.Add(CreateAccessorFunction(classSymbol, propertySymbol, propertySyntax, accessor, true, ctx, className));
                    break;

                case SyntaxKind.SetAccessorDeclaration:
                    accessorFunctions.Add(CreateAccessorFunction(classSymbol, propertySymbol, propertySyntax, accessor, false, ctx, className));
                    break;
            }
        }

        if (accessorFunctions.Count == 0) {
            return false;
        }

        functions = accessorFunctions;
        return true;
    }

    private static FunctionDeclaration CreateAccessorFunction(
        INamedTypeSymbol classSymbol,
        IPropertySymbol propertySymbol,
        PropertyDeclarationSyntax propertySyntax,
        AccessorDeclarationSyntax accessor,
        bool isGetter,
        TranspilationContext ctx,
        string className
    ) {
        var methodName = isGetter ? $"Get{propertySymbol.Name}" : $"Set{propertySymbol.Name}";
        var separator = propertySymbol.IsStatic ? '.' : ':';
        var functionName = FunctionName.FromString($"{className}{separator}{methodName}");

        var parameters = new List<Parameter>();
        var typeSpecifiers = new List<TypeInfo>();

        if (!isGetter) {
            parameters.Add(NameParameter.FromString("value"));
            typeSpecifiers.Add(SyntaxUtilities.BasicFromSymbol(propertySymbol.Type));
        }

        var returnType = isGetter
            ? SyntaxUtilities.BasicFromSymbol(propertySymbol.Type)
            : BasicTypeInfo.Void();

        var body = BuildAccessorBlock(propertySymbol, accessor, ctx, isGetter);

        return new FunctionDeclaration {
            Name = functionName,
            Body = new FunctionBody {
                Parameters = parameters,
                TypeSpecifiers = typeSpecifiers,
                ReturnType = returnType,
                Body = body,
            },
        };
    }

    private static Block BuildAccessorBlock(
        IPropertySymbol propertySymbol,
        AccessorDeclarationSyntax accessor,
        TranspilationContext ctx,
        bool returnsValue)
    {
        var block = Block.Empty();

        ctx.PushScope();

        if (accessor.Body is not null) {
            foreach (var statementSyntax in accessor.Body.Statements) {
                var statement = StatementBuilder.Transpile(statementSyntax, ctx);
                ctx.AppendPrerequisites(block);
                block.AddStatement(statement);
            }

            ctx.AppendPrerequisites(block);
        } else if (accessor.ExpressionBody is not null) {
            if (returnsValue) {
                var expression = ExpressionBuilder.BuildFromSyntax(accessor.ExpressionBody.Expression, ctx);
                ctx.AppendPrerequisites(block);
                block.AddStatement(Return.FromExpressions([expression]));
            } else {
                var expressionStatement = SyntaxFactory.ExpressionStatement(accessor.ExpressionBody.Expression);
                var statement = StatementBuilder.Transpile(expressionStatement, ctx);
                ctx.AppendPrerequisites(block);
                block.AddStatement(statement);
            }
        }

        ctx.PopScope();

        return block;
    }

}
