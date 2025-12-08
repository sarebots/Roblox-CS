using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Types;
using RobloxCS.Shared;

namespace RobloxCS.TranspilerV2.Builders;

internal static class FieldBuilder {
    public static IEnumerable<TypeField> GenerateTypeFieldsFromField(FieldDeclarationSyntax fieldSyntax, TranspilationContext ctx) {
        var decl = fieldSyntax.Declaration;
        var fieldType = InferNonnull(decl.Type, ctx);
        var isReadonly = fieldSyntax.Modifiers.Any(SyntaxKind.ReadOnlyKeyword);

        foreach (var v in decl.Variables) {
            yield return new TypeField {
                Key = NameTypeFieldKey.FromString(v.Identifier.ValueText),
                Access = isReadonly ? AccessModifier.Read : null,
                Value = SyntaxUtilities.TypeInfoFromSymbol(fieldType),
            };
        }
    }

    public static IEnumerable<Assignment> CreateFieldAssignmentsFromFields(IEnumerable<IFieldSymbol> fields, TranspilationContext ctx) {
        var orderedFields = fields
            .Where(field => !field.IsStatic)
            .OrderBy(field => AccessibilityRank(field.DeclaredAccessibility))
            .ThenBy(field => field.Name, StringComparer.Ordinal);

        foreach (var field in orderedFields) {
            foreach (var declRef in field.DeclaringSyntaxReferences) {
                if (declRef.GetSyntax() is not VariableDeclaratorSyntax v) continue;

                var init = v.Initializer;
                Expression? rhs = null;

                if (init is not null) {
                    rhs = ExpressionBuilder.BuildFromSyntax(init.Value, ctx);
                } else {
                    rhs = DefaultValueBuilder.CreateDefaultValueExpression(field.Type, ctx);
                }

                if (rhs is null) continue;

                yield return new Assignment {
                    Vars = [VarName.FromString($"self.{field.Name}")],
                    Expressions = [rhs],
                };
            }
        }
    }

    private static int AccessibilityRank(Accessibility accessibility) => accessibility switch {
        Accessibility.Public => 0,
        Accessibility.ProtectedOrInternal => 1,
        Accessibility.Internal => 2,
        Accessibility.Protected => 3,
        Accessibility.ProtectedAndInternal => 4,
        Accessibility.Private => 5,
        _ => 6,
    };

    private static ITypeSymbol InferNonnull(TypeSyntax syntax, TranspilationContext ctx) {
        var fieldType = ctx.Semantics.GetTypeInfo(syntax).Type!;
        if (fieldType is IErrorTypeSymbol or null) {
            throw new Exception("Error occured while attempting to infer type.");
        }

        Logger.Debug($"Inferred type {fieldType.Name} from {syntax}");

        return fieldType;
    }
}
