using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Statements;

namespace RobloxCS.TranspilerV2.Builders;

internal static class StaticMemberBuilder {
    public static IEnumerable<Statement> CreateStaticFieldAssignments(
        INamedTypeSymbol classSymbol,
        FieldDeclarationSyntax fieldDeclaration,
        TranspilationContext ctx,
        string className
    ) {
        if (!fieldDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword) || m.IsKind(SyntaxKind.ConstKeyword))) {
            return Enumerable.Empty<Statement>();
        }

        var assignments = new List<Statement>();

        foreach (var variable in fieldDeclaration.Declaration.Variables) {
            if (variable.Initializer is null) {
                continue;
            }

            var expression = ExpressionBuilder.BuildFromSyntax(variable.Initializer.Value, ctx);

            assignments.Add(new Assignment
            {
                Vars = [VarName.FromString($"{className}.{variable.Identifier.ValueText}")],
                Expressions = [expression],
            });
        }

        return assignments;
    }

    public static bool TryCreateStaticPropertyAssignment(
        INamedTypeSymbol classSymbol,
        PropertyDeclarationSyntax propertyDeclaration,
        TranspilationContext ctx,
        string className,
        out Statement assignment
    ) {
        assignment = default!;

        if (!propertyDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword) || propertyDeclaration.Initializer is null) {
            return false;
        }

        var expression = ExpressionBuilder.BuildFromSyntax(propertyDeclaration.Initializer.Value, ctx);

        assignment = new Assignment
        {
            Vars = [VarName.FromString($"{className}.{propertyDeclaration.Identifier.ValueText}")],
            Expressions = [expression],
        };

        return true;
    }
}
