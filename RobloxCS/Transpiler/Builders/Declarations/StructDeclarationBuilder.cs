using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Functions;
using RobloxCS.AST.Generics;
using RobloxCS.AST.Parameters;
using RobloxCS.AST.Prefixes;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Suffixes;
using RobloxCS.AST.Types;
using RobloxCS.Shared;
using TypeInfo = RobloxCS.AST.Types.TypeInfo;
using Expression = RobloxCS.AST.Expressions.Expression;
using AttributeData = Microsoft.CodeAnalysis.AttributeData;
using TypedConstant = Microsoft.CodeAnalysis.TypedConstant;

namespace RobloxCS.TranspilerV2.Builders.Declarations;

internal static class StructDeclarationBuilder
{
    private sealed record StructMemberInfo(
        string Name,
        TypeInfo Type,
        bool IsTypeParameter,
        Expression? DefaultExpression,
        IReadOnlyList<Statement> Prerequisites,
        AccessModifier? Access,
        bool IsIndexer = false,
        TypeInfo? IndexKeyType = null);

    private sealed record StructConstructorInfo(
        IReadOnlyList<Parameter> Parameters,
        IReadOnlyList<TypeInfo> TypeSpecifiers,
        IReadOnlyList<Statement> BodyStatements);

    private readonly record struct StaticEventAssignmentInfo(bool AssignedNull, bool UsesSignal);

    public static bool TryBuild(
        StructDeclarationSyntax node,
        INamedTypeSymbol structSymbol,
        TranspilationContext ctx,
        out List<Statement> statements)
    {
        statements = [];

        if (structSymbol.IsRefLikeType)
        {
            return false;
        }

        if (!TryCollectMembers(node, structSymbol, ctx, out var members, out var constructorInfo, out var staticStatements))
        {
            return false;
        }

        var structName = ctx.GetTypeName(structSymbol);
        ctx.EnsureTypePredeclaration(structName);

        var predeclarations = new List<Statement>();
        TryAddPredeclaration(predeclarations, ctx, structName);

        var orderedStatic = staticStatements.OrderBy(tuple => tuple.Index).Select(tuple => tuple.Statement);

        statements.AddRange(predeclarations);
        statements.Add(CreateFactoryAssignment(structName, structSymbol, members, constructorInfo));
        if (constructorInfo is not null)
        {
            statements.Add(CreateNewMethod(structName, structSymbol, constructorInfo));
        }
        statements.AddRange(orderedStatic);
        statements.AddRange(CreateMetadataAssignments(ctx, structSymbol, structName));
        statements.Add(CreateDefineGlobalCall(structName));
        statements.Add(CreateTypeAlias(ctx, structName, structSymbol, members));
        statements.Add(Return.FromExpressions([SymbolExpression.FromString("nil")]));

        return true;
    }

    private static void TryAddPredeclaration(List<Statement> predeclarations, TranspilationContext ctx, string typeName)
    {
        if (ctx.TryConsumeTypePredeclaration(typeName, out var statement) && statement is not null)
        {
            predeclarations.Add(statement);
        }
    }

    private static bool TryCollectMembers(
        StructDeclarationSyntax node,
        INamedTypeSymbol structSymbol,
        TranspilationContext ctx,
        out List<StructMemberInfo> members,
        out StructConstructorInfo? constructorInfo,
        out List<(int Index, Statement Statement)> staticStatements)
    {
        members = [];
        constructorInfo = null;
        staticStatements = [];
        var structName = ctx.GetTypeName(structSymbol);
        var staticEventAssignments = CollectStaticEventAssignments(node, structSymbol, ctx);

        for (var i = 0; i < node.Members.Count; i++)
        {
            var memberSyntax = node.Members[i];

            if (memberSyntax is ConstructorDeclarationSyntax ctorDeclaration &&
                ctorDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                foreach (var statement in BuildStaticConstructorStatements(ctx, ctorDeclaration, structSymbol, structName))
                {
                    staticStatements.Add((i, statement));
                }

                continue;
            }

            if (memberSyntax is FieldDeclarationSyntax fieldDeclaration)
            {
                foreach (var variable in fieldDeclaration.Declaration.Variables)
                {
                    if (ctx.Semantics.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
                    {
                        continue;
                    }

                    if (fieldSymbol.IsStatic)
                    {
                        foreach (var assignment in StaticMemberBuilder.CreateStaticFieldAssignments(structSymbol, fieldDeclaration, ctx, structName))
                        {
                            staticStatements.Add((i, assignment));
                        }

                        continue;
                    }

                    if (!TryCreateFieldMember(fieldSymbol, variable, ctx, out var member))
                    {
                        return false;
                    }

                    members.Add(member);
                }

                continue;
            }

            if (memberSyntax is PropertyDeclarationSyntax propertyDeclaration)
            {
                if (ctx.Semantics.GetDeclaredSymbol(propertyDeclaration) is not IPropertySymbol propertySymbol)
                {
                    continue;
                }

                if (propertySymbol.IsStatic)
                {
                    if (StaticMemberBuilder.TryCreateStaticPropertyAssignment(structSymbol, propertyDeclaration, ctx, structName, out var assignment))
                    {
                        staticStatements.Add((i, assignment));
                    }

                    continue;
                }

                if (!TryCreatePropertyMember(propertySymbol, propertyDeclaration, ctx, out var member))
                {
                    Logger.Warn($"Struct '{structName}' contains a non-auto or unsupported property '{propertySymbol.Name}'; falling back to class lowering.");
                    return false;
                }

                members.Add(member);
                continue;
            }

            if (memberSyntax is MethodDeclarationSyntax methodDeclaration)
            {
                if (ctx.Semantics.GetDeclaredSymbol(methodDeclaration) is not IMethodSymbol methodSymbol)
                {
                    continue;
                }

                if (methodSymbol.IsStatic && methodSymbol.MethodKind is MethodKind.Ordinary)
                {
                    var statement = FunctionBuilder.CreateStaticMethod(structSymbol, methodSymbol, methodDeclaration, ctx, structName);
                    staticStatements.Add((i, statement));
                }
                else if (methodSymbol.MethodKind is MethodKind.Ordinary)
                {
                    Logger.Warn($"Struct '{structName}' contains an instance method '{methodSymbol.Name}'; falling back to class lowering.");
                    return false;
                }

                continue;
            }

            if (memberSyntax is ConstructorDeclarationSyntax staticCtorDeclaration &&
                staticCtorDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                foreach (var statement in BuildStaticConstructorStatements(ctx, staticCtorDeclaration, structSymbol, structName))
                {
                    staticStatements.Add((i, statement));
                }

                continue;
            }

            if (memberSyntax is EventDeclarationSyntax eventDeclaration)
            {
                if (ctx.Semantics.GetDeclaredSymbol(eventDeclaration) is not IEventSymbol eventSymbol)
                {
                    continue;
                }

                if (eventSymbol.IsStatic)
                {
                    if (staticEventAssignments.TryGetValue(eventSymbol.Name, out var assignmentInfo))
                    {
                        if (assignmentInfo.UsesSignal)
                        {
                            ctx.EnsureSignalImport();
                        }

                        if (assignmentInfo.AssignedNull)
                        {
                            continue;
                        }
                        // Respect explicit static constructor initialization and skip the default Signal.new assignment.
                        continue;
                    }

                    ctx.EnsureSignalImport();
                    staticStatements.Add((i, new Assignment
                    {
                        Vars = [VarName.FromString($"{structName}.{eventSymbol.Name}")],
                        Expressions = [FunctionCall.Basic("Signal.new")],
                    }));
                }
                else
                {
                    Logger.Warn($"Struct '{structName}' contains an instance event '{eventSymbol.Name}'; falling back to class lowering.");
                    return false;
                }
            }

            if (memberSyntax is EventFieldDeclarationSyntax eventField)
            {
                foreach (var variable in eventField.Declaration.Variables)
                {
                    if (ctx.Semantics.GetDeclaredSymbol(variable) is not IEventSymbol eventSymbol)
                    {
                        continue;
                    }

                    if (eventSymbol.IsStatic)
                    {
                        if (staticEventAssignments.TryGetValue(eventSymbol.Name, out var assignmentInfo))
                        {
                            if (assignmentInfo.UsesSignal)
                            {
                                ctx.EnsureSignalImport();
                            }

                            if (assignmentInfo.AssignedNull)
                            {
                                continue;
                            }

                            continue;
                        }

                        ctx.EnsureSignalImport();
                        staticStatements.Add((i, new Assignment
                        {
                            Vars = [VarName.FromString($"{structName}.{eventSymbol.Name}")],
                            Expressions = [FunctionCall.Basic("Signal.new")],
                        }));
                    }
                    else
                    {
                        Logger.Warn($"Struct '{structName}' contains an instance event '{eventSymbol.Name}'; falling back to class lowering.");
                        return false;
                    }
                }
            }

            if (memberSyntax is IndexerDeclarationSyntax indexerDeclaration)
            {
                if (ctx.Semantics.GetDeclaredSymbol(indexerDeclaration) is not IPropertySymbol indexerSymbol)
                {
                    continue;
                }

                if (TryCreateIndexerMember(indexerSymbol, indexerDeclaration, ctx, out var member))
                {
                    members.Add(member);
                }
            }
        }

        constructorInfo = TryCreateConstructor(node, structSymbol, ctx);

        if (members.Count == 0)
        {
            Logger.Warn($"Struct '{structSymbol.Name}' has no instance data; emitting empty factory.");
        }

        return true;
    }

    private static StructConstructorInfo? TryCreateConstructor(
        StructDeclarationSyntax node,
        INamedTypeSymbol structSymbol,
        TranspilationContext ctx)
    {
        var constructorSymbol = structSymbol.InstanceConstructors.FirstOrDefault(ctor => !ctor.IsImplicitlyDeclared && !ctor.IsStatic);
        if (constructorSymbol is null)
        {
            return null;
        }

        var parameters = new List<Parameter>();
        var typeSpecifiers = new List<TypeInfo>();

        foreach (var parameter in constructorSymbol.Parameters)
        {
            parameters.Add(NameParameter.FromString(parameter.Name));
            typeSpecifiers.Add(SyntaxUtilities.BasicFromSymbol(parameter.Type));
        }

        var bodyStatements = new List<Statement>();
        var constructorSyntax = constructorSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as ConstructorDeclarationSyntax;
        if (constructorSyntax?.Body is not null)
        {
            ctx.PushScope();
            foreach (var statement in constructorSyntax.Body.Statements)
            {
                var transpiled = StatementBuilder.Transpile(statement, ctx);
                ctx.AppendPrerequisites(bodyStatements);
                bodyStatements.Add(transpiled);
            }
            ctx.AppendPrerequisites(bodyStatements);
            ctx.PopScope();
        }

        return new StructConstructorInfo(parameters, typeSpecifiers, bodyStatements);
    }

    private static bool TryCreateFieldMember(
        IFieldSymbol fieldSymbol,
        VariableDeclaratorSyntax variable,
        TranspilationContext ctx,
        out StructMemberInfo member)
    {
        member = null!;

        var type = SyntaxUtilities.BasicFromSymbol(fieldSymbol.Type);
        var isTypeParameter = fieldSymbol.Type.TypeKind == TypeKind.TypeParameter;
        Expression? defaultExpression = null;
        var prerequisites = new List<Statement>();

        if (variable.Initializer is { Value: { } initializerSyntax })
        {
            var constantValue = ctx.Semantics.GetConstantValue(initializerSyntax);
            var initializerExpression = ExpressionBuilder.BuildFromSyntax(initializerSyntax, ctx);
            var consumed = ctx.ConsumePrerequisites();
            prerequisites.AddRange(consumed);

            if (constantValue.HasValue)
            {
                defaultExpression = initializerExpression;
            }
            else
            {
                var tempName = ctx.AllocateTempName(variable.Identifier.Text, "default");
                prerequisites.Add(new LocalAssignment
                {
                    Names = [SymbolExpression.FromString(tempName)],
                    Expressions = [initializerExpression],
                    Types = [],
                });

                defaultExpression = SymbolExpression.FromString(tempName);
            }
        }

        member = new StructMemberInfo(
            variable.Identifier.Text,
            type,
            isTypeParameter,
            defaultExpression,
            prerequisites,
            null);

        return true;
    }

    private static bool TryCreatePropertyMember(
        IPropertySymbol propertySymbol,
        PropertyDeclarationSyntax propertySyntax,
        TranspilationContext ctx,
        out StructMemberInfo member)
    {
        member = null!;

        var isIndexer = propertySymbol.Parameters.Length > 0;

        if (!isIndexer && !IsAutoProperty(propertySyntax))
        {
            return false;
        }

        var type = SyntaxUtilities.BasicFromSymbol(propertySymbol.Type);
        var isTypeParameter = propertySymbol.Type.TypeKind == TypeKind.TypeParameter;

        Expression? defaultExpression = null;
        var prerequisites = new List<Statement>();

        if (!isIndexer && propertySyntax.Initializer is { Value: { } initializerSyntax })
        {
            var initializerExpression = ExpressionBuilder.BuildFromSyntax(initializerSyntax, ctx);
            var consumed = ctx.ConsumePrerequisites();
            prerequisites.AddRange(consumed);

            var constantValue = ctx.Semantics.GetConstantValue(initializerSyntax);
            if (constantValue.HasValue)
            {
                defaultExpression = initializerExpression;
            }
            else
            {
                var tempName = ctx.AllocateTempName(propertySymbol.Name, "default");
                prerequisites.Add(new LocalAssignment
                {
                    Names = [SymbolExpression.FromString(tempName)],
                    Expressions = [initializerExpression],
                    Types = [],
                });

                defaultExpression = SymbolExpression.FromString(tempName);
            }
        }

        AccessModifier? access = null;
        if (propertySymbol.GetMethod is not null && propertySymbol.SetMethod is null)
        {
            access = AccessModifier.Read;
        }
        else if (propertySymbol.GetMethod is null && propertySymbol.SetMethod is not null)
        {
            access = AccessModifier.Write;
        }

        member = new StructMemberInfo(
            propertySymbol.Name,
            type,
            isTypeParameter,
            defaultExpression,
            prerequisites,
            access,
            isIndexer,
            isIndexer && propertySymbol.Parameters.Length > 0
                ? SyntaxUtilities.TypeInfoFromSymbol(propertySymbol.Parameters[0].Type)
                : null);

        return true;
    }

    private static bool TryCreateIndexerMember(
        IPropertySymbol indexerSymbol,
        IndexerDeclarationSyntax indexerSyntax,
        TranspilationContext ctx,
        out StructMemberInfo member)
    {
        member = null!;

        var keyType = indexerSymbol.Parameters.Length > 0
            ? SyntaxUtilities.TypeInfoFromSymbol(indexerSymbol.Parameters[0].Type)
            : BasicTypeInfo.FromString("any");

        AccessModifier? access = null;
        if (indexerSymbol.GetMethod is not null && indexerSymbol.SetMethod is null)
        {
            access = AccessModifier.Read;
        }
        else if (indexerSymbol.GetMethod is null && indexerSymbol.SetMethod is not null)
        {
            access = AccessModifier.Write;
        }

        member = new StructMemberInfo(
            indexerSymbol.Name,
            SyntaxUtilities.TypeInfoFromSymbol(indexerSymbol.Type),
            indexerSymbol.Type.TypeKind == TypeKind.TypeParameter,
            null,
            Array.Empty<Statement>(),
            access,
            true,
            keyType);

        return true;
    }

    private static IEnumerable<Statement> BuildStaticConstructorStatements(
        TranspilationContext ctx,
        ConstructorDeclarationSyntax constructorDeclaration,
        INamedTypeSymbol structSymbol,
        string structName)
    {
        if (constructorDeclaration.Body is not { } body)
        {
            yield break;
        }

        ctx.PushScope();
        var block = Block.Empty();

        foreach (var statementSyntax in body.Statements)
        {
            var statement = StatementBuilder.Transpile(statementSyntax, ctx);
            ctx.AppendPrerequisites(block);
            block.AddStatement(statement);
        }

        ctx.AppendPrerequisites(block);
        ctx.PopScope();

        // Qualify any static assignment targets encountered in the static constructor body.
        var staticFields = structSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.IsStatic)
            .Select(f => f.Name)
            .ToHashSet(StringComparer.Ordinal);

        var staticProperties = structSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.IsStatic)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var staticEvents = structSymbol.GetMembers()
            .OfType<IEventSymbol>()
            .Where(e => e.IsStatic)
            .Select(e => e.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var statement in block.Statements)
        {
            if (statement is Assignment assignment)
            {
                for (var i = 0; i < assignment.Vars.Count; i++)
                {
                    if (assignment.Vars[i] is VarName { Name: { } name } &&
                        (staticFields.Contains(name) || staticProperties.Contains(name) || staticEvents.Contains(name)))
                    {
                        assignment.Vars[i] = VarName.FromString($"{structName}.{name}");
                    }
                }
            }

            yield return statement;
        }
    }

    private static Dictionary<string, StaticEventAssignmentInfo> CollectStaticEventAssignments(
        StructDeclarationSyntax node,
        INamedTypeSymbol structSymbol,
        TranspilationContext ctx)
    {
        var assignedEvents = new Dictionary<string, StaticEventAssignmentInfo>(StringComparer.Ordinal);

        foreach (var ctor in node.Members.OfType<ConstructorDeclarationSyntax>()
                     .Where(ctor => ctor.Modifiers.Any(SyntaxKind.StaticKeyword)))
        {
            if (ctor.Body is null)
            {
                continue;
            }

            foreach (var assignment in ctor.Body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Kind() is not SyntaxKind.SimpleAssignmentExpression)
                {
                    continue;
                }

                var symbolInfo = ctx.Semantics.GetSymbolInfo(assignment.Left);
                var eventSymbol = symbolInfo.Symbol as IEventSymbol
                    ?? symbolInfo.CandidateSymbols.OfType<IEventSymbol>().FirstOrDefault()
                    ?? TryMatchEventByName(structSymbol, assignment.Left);

                if (eventSymbol is null)
                {
                    continue;
                }

                if (eventSymbol is { IsStatic: true })
                {
                    var usesSignal = UsesSignal(assignment.Right, structSymbol, ctx);
                    var info = assignedEvents.GetValueOrDefault(eventSymbol.Name);
                    assignedEvents[eventSymbol.Name] = new StaticEventAssignmentInfo(
                        info.AssignedNull || assignment.Right.IsKind(SyntaxKind.NullLiteralExpression),
                        info.UsesSignal || usesSignal);
                }
            }
        }

        return assignedEvents;
    }

    private static IEventSymbol? TryMatchEventByName(INamedTypeSymbol structSymbol, ExpressionSyntax left)
    {
        string? name = left switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => null,
        };

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return structSymbol.GetMembers()
            .OfType<IEventSymbol>()
            .FirstOrDefault(evt => evt.IsStatic && string.Equals(evt.Name, name, StringComparison.Ordinal));
    }

    private static ITypeSymbol? TryGetAliasTarget(ExpressionSyntax expression, TranspilationContext ctx)
    {
        foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            var alias = ctx.Semantics.GetAliasInfo(identifier);
            if (alias?.Target is ITypeSymbol aliasType)
            {
                return aliasType;
            }
        }

        return null;
    }

    private static bool UsesSignal(ExpressionSyntax expression, INamedTypeSymbol structSymbol, TranspilationContext ctx)
    {
        if (TryGetAliasTarget(expression, ctx) is ITypeSymbol aliasType &&
            aliasType.Locations.Any(loc => loc.IsInSource))
        {
            return false;
        }

        var symbol = ctx.Semantics.GetSymbolInfo(expression).Symbol;
        if (symbol is IMethodSymbol methodSymbol &&
            methodSymbol.Locations.Any(loc => loc.IsInSource))
        {
            return false;
        }

        if (symbol is ITypeSymbol typeSymbol &&
            typeSymbol.Locations.Any(loc => loc.IsInSource))
        {
            return false;
        }

        if (symbol is IMethodSymbol signalMethodSymbol &&
            string.Equals(signalMethodSymbol.ContainingType?.Name, "Signal", StringComparison.Ordinal) &&
            !SymbolEqualityComparer.Default.Equals(signalMethodSymbol.ContainingType?.ContainingType, structSymbol))
        {
            return true;
        }

        if (expression is InvocationExpressionSyntax invocation)
        {
            return ReferencesSignal(invocation.Expression, structSymbol, ctx);
        }

        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return ReferencesSignal(memberAccess.Expression, structSymbol, ctx);
        }

        return expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
            .Any(identifier => ReferencesSignal(identifier, structSymbol, ctx));
    }

    private static bool ReferencesSignal(ExpressionSyntax expression, INamedTypeSymbol structSymbol, TranspilationContext ctx)
    {
        if (expression is IdentifierNameSyntax identifier &&
            string.Equals(identifier.Identifier.ValueText, "Signal", StringComparison.Ordinal))
        {
            var symbol = ctx.Semantics.GetSymbolInfo(identifier).Symbol;
            if (symbol is ITypeSymbol typeSymbol &&
                (SymbolEqualityComparer.Default.Equals(typeSymbol, structSymbol)
                 || SymbolEqualityComparer.Default.Equals(typeSymbol.ContainingType, structSymbol)))
            {
                return false;
            }

            if (symbol is IMethodSymbol methodSymbol &&
                SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType?.ContainingType, structSymbol))
            {
                return false;
            }

            return true;
        }

        if (expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax id } &&
            string.Equals(id.Identifier.ValueText, "Signal", StringComparison.Ordinal))
        {
            var symbol = ctx.Semantics.GetSymbolInfo(id).Symbol;
            if (symbol is ITypeSymbol typeSymbol &&
                (SymbolEqualityComparer.Default.Equals(typeSymbol, structSymbol)
                 || SymbolEqualityComparer.Default.Equals(typeSymbol.ContainingType, structSymbol)))
            {
                return false;
            }

            if (symbol is IMethodSymbol methodSymbol &&
                SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType?.ContainingType, structSymbol))
            {
                return false;
            }

            return true;
        }

        return expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
            .Any(identifier => string.Equals(identifier.Identifier.ValueText, "Signal", StringComparison.Ordinal));
    }

    private static bool IsAutoProperty(PropertyDeclarationSyntax propertySyntax)
    {
        if (propertySyntax.AccessorList is null)
        {
            return false;
        }

        foreach (var accessor in propertySyntax.AccessorList.Accessors)
        {
            if (accessor.Body is not null || accessor.ExpressionBody is not null)
            {
                return false;
            }
        }

        return true;
    }

    private static Statement CreateFactoryAssignment(
        string structName,
        INamedTypeSymbol structSymbol,
        List<StructMemberInfo> members,
        StructConstructorInfo? constructorInfo)
    {
        var parameterList = members
            .Where(member => !member.IsIndexer)
            .Select(member => NameParameter.FromString(member.Name))
            .ToList();

        var typeSpecifiers = members
            .Where(member => !member.IsIndexer)
            .Select(member => member.IsTypeParameter ? BasicTypeInfo.FromString("any") : member.Type)
            .ToList();

        var tableFields = new List<TableField>();

        foreach (var member in members.Where(member => !member.IsIndexer))
        {
            Expression valueExpression = SymbolExpression.FromString(member.Name);
            if (member.DefaultExpression is not null)
            {
                valueExpression = new BinaryOperatorExpression
                {
                    Left = valueExpression,
                    Right = member.DefaultExpression,
                    Op = BinOp.Or,
                };
            }

            tableFields.Add(new NameKey
            {
                Key = member.Name,
                Value = valueExpression,
            });
        }

        var tableConstructor = new TableConstructor
        {
            Fields = tableFields,
        };

        var body = Block.Empty();

        foreach (var member in members.Where(member => !member.IsIndexer))
        {
            foreach (var prereq in member.Prerequisites)
            {
                body.AddStatement(prereq);
            }
        }

        body.AddStatement(Return.FromExpressions([tableConstructor]));

        var anonymousFunction = new AnonymousFunction
        {
            Body = new FunctionBody
            {
                Parameters = constructorInfo is { Parameters.Count: > 0 }
                    ? constructorInfo.Parameters.Concat(parameterList.Cast<Parameter>()).ToList()
                    : parameterList.Cast<Parameter>().ToList(),
                TypeSpecifiers = constructorInfo is { TypeSpecifiers.Count: > 0 }
                    ? constructorInfo.TypeSpecifiers.Concat(typeSpecifiers).ToList()
                    : typeSpecifiers,
                ReturnType = BasicTypeInfo.FromString(structName),
                Body = body,
            },
        };

        return new LocalAssignment
        {
            Names = [SymbolExpression.FromString(structName)],
            Expressions = [anonymousFunction],
            Types = [],
        };
    }

    private static Statement CreateDefineGlobalCall(string structName)
    {
        return new FunctionCallStatement
        {
            Prefix = NamePrefix.FromString("CS.defineGlobal"),
            Suffixes =
            [
                new AnonymousCall
                {
                    Arguments = new FunctionArgs
                    {
                        Arguments =
                        [StringExpression.FromString(structName), SymbolExpression.FromString(structName)],
                    },
                },
            ],
        };
    }

    private static Statement CreateTypeAlias(TranspilationContext ctx, string structName, INamedTypeSymbol structSymbol, List<StructMemberInfo> members)
    {
        var tableType = new TableTypeInfo
        {
            Fields = members.Select(member =>
            {
                if (member.IsIndexer && member.IndexKeyType is not null)
                {
                    return new TypeField
                    {
                        Key = IndexSignatureTypeFieldKey.FromInfo(member.IndexKeyType),
                        Value = member.Type,
                        Access = member.Access,
                    };
                }

                var field = TypeField.FromNameAndType(member.Name, member.Type);
                field.Access = member.Access;
                return field;
            }).ToList(),
        };

        var declaration = new TypeDeclaration
        {
            Name = structName,
            DeclareAs = tableType,
        };

        if (structSymbol.TypeParameters.Length > 0)
        {
            declaration.Declarations = [CreateGenericDeclaration(ctx, structSymbol.TypeParameters)];
        }

        return declaration;
    }

    private static Statement CreateNewMethod(string structName, INamedTypeSymbol structSymbol, StructConstructorInfo constructorInfo)
    {
        var parameters = new List<Parameter> { NameParameter.FromString("self") };
        parameters.AddRange(constructorInfo.Parameters);

        var typeSpecifiers = new List<TypeInfo> { SyntaxUtilities.BasicFromSymbol(structSymbol) };
        typeSpecifiers.AddRange(constructorInfo.TypeSpecifiers);

        var body = Block.Empty();
        foreach (var statement in constructorInfo.BodyStatements)
        {
            body.AddStatement(statement);
        }

        body.AddStatement(Return.FromExpressions([SymbolExpression.FromString("self")]));

        return new FunctionDeclaration
        {
            Name = FunctionName.FromString($"{structName}.new"),
            Body = new FunctionBody
            {
                Parameters = parameters,
                TypeSpecifiers = typeSpecifiers,
                ReturnType = SyntaxUtilities.BasicFromSymbol(structSymbol),
                Body = body,
            },
        };
    }

    private static IEnumerable<Statement> CreateMetadataAssignments(TranspilationContext ctx, INamedTypeSymbol structSymbol, string structName)
    {
        var statements = new List<Statement>();

        var interfaceFields = BuildInterfaceFields(ctx, structSymbol.Interfaces);

        if (interfaceFields.Count > 0)
        {
            statements.Add(new Assignment
            {
                Vars = [VarName.FromString($"{structName}.__interfaces")],
                Expressions = [new TableConstructor { Fields = interfaceFields, PadEntries = true }],
            });
        }

        var attributeFields = BuildAttributeFields(ctx, structSymbol.GetAttributes());

        if (attributeFields.Count > 0)
        {
            statements.Add(new Assignment
            {
                Vars = [VarName.FromString($"{structName}.__attributes")],
                Expressions = [new TableConstructor { Fields = attributeFields, PadEntries = true }],
            });
        }

        return statements;
    }

    private static GenericDeclaration CreateGenericDeclaration(TranspilationContext ctx, IEnumerable<ITypeParameterSymbol> typeParameters)
    {
        return new GenericDeclaration
        {
            Parameters = typeParameters.Select(tp => CreateGenericParameter(ctx, tp)).ToList(),
        };
    }

    private static GenericDeclarationParameter CreateGenericParameter(TranspilationContext ctx, ITypeParameterSymbol typeParameter)
    {
        var parameter = new GenericDeclarationParameter
        {
            Parameter = NameGenericParameter.FromString(typeParameter.Name),
        };

        var constraintTypes = new List<TypeInfo>();
        var simpleConstraints = new HashSet<string>(StringComparer.Ordinal);

        void AddSimpleConstraint(string name)
        {
            if (simpleConstraints.Add(name))
            {
                constraintTypes.Add(BasicTypeInfo.FromString(name));
            }
        }

        foreach (var constraint in typeParameter.ConstraintTypes)
        {
            if (constraint is INamedTypeSymbol namedConstraint)
            {
                var constraintName = ctx.GetTypeName(namedConstraint);
                ctx.EnsureTypePredeclaration(constraintName);
            }

            constraintTypes.Add(SyntaxUtilities.TypeInfoFromSymbol(constraint));
        }

        if (typeParameter.HasReferenceTypeConstraint)
        {
            AddSimpleConstraint("class");
        }

        if (typeParameter.HasValueTypeConstraint)
        {
            AddSimpleConstraint("struct");
        }

        if (typeParameter.HasNotNullConstraint)
        {
            AddSimpleConstraint("notnull");
        }

        if (typeParameter.HasUnmanagedTypeConstraint)
        {
            AddSimpleConstraint("unmanaged");
        }

        if (typeParameter.HasConstructorConstraint)
        {
            var constructorType = new CallbackTypeInfo
            {
                Arguments = [],
                ReturnType = BasicTypeInfo.FromString(typeParameter.Name),
            };

            constraintTypes.Add(constructorType);
        }

        if (constraintTypes.Count == 1)
        {
            parameter.Constraint = constraintTypes[0];
        }
        else if (constraintTypes.Count > 1)
        {
            parameter.Constraint = IntersectionTypeInfo.FromTypes(constraintTypes);
        }

        return parameter;
    }

    private static List<TableField> BuildInterfaceFields(TranspilationContext ctx, IEnumerable<INamedTypeSymbol> interfaces)
    {
        return interfaces
            .Where(@interface => @interface.TypeKind == TypeKind.Interface)
            .Select(@interface =>
            {
                var interfaceName = ctx.GetTypeName(@interface);
                ctx.EnsureTypePredeclaration(interfaceName);
                var entry = new TableConstructor
                {
                    Fields =
                    [
                        new NoKey { Expression = SymbolExpression.FromString(interfaceName) },
                    ],
                    PadEntries = true,
                };

                return (TableField)new NoKey { Expression = entry };
            })
            .ToList();
    }

    private static List<TableField> BuildAttributeFields(TranspilationContext ctx, IEnumerable<AttributeData> attributes)
    {
        var fields = new List<TableField>();

        foreach (var attribute in attributes)
        {
            if (attribute.AttributeClass is null)
            {
                continue;
            }

            var attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax
                ?? FindAttributeSyntax(attribute, ctx);
            fields.Add(new NoKey { Expression = CreateAttributeEntry(ctx, attribute, attributeSyntax) });
        }

        return fields;
    }

    private static TableConstructor CreateAttributeEntry(TranspilationContext ctx, AttributeData attribute, AttributeSyntax? attributeSyntax)
    {
        var fields = new List<TableField>
        {
            new NoKey { Expression = StringExpression.FromString(attribute.AttributeClass!.Name) },
        };

        var positionalArguments = new List<TableField>();
        var namedFields = new List<TableField>();

        var semanticNamedArguments = attribute.NamedArguments
            .GroupBy(arg => arg.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);

        if (attributeSyntax?.ArgumentList is { Arguments.Count: > 0 } syntaxArguments)
        {
            var arguments = syntaxArguments.Arguments;
            for (var i = 0; i < arguments.Count; i++)
            {
                var argument = arguments[i];
                if (TryCreateNamedAttributeField(argument, ctx, semanticNamedArguments, out var namedField) ||
                    TryCreateSplitNamedAttributeField(arguments, ref i, ctx, semanticNamedArguments, out namedField))
                {
                    namedFields.Add(namedField);
                    continue;
                }

                positionalArguments.Add(new NoKey
                {
                    Expression = CreateAttributeArgumentExpressionFromSyntax(argument, ctx),
                });
            }
        }
        else
        {
            positionalArguments.AddRange(attribute.ConstructorArguments
                .Select(argument => (TableField)new NoKey
                {
                    Expression = CreateAttributeArgumentExpression(ctx, argument),
                }));

            namedFields.AddRange(attribute.NamedArguments
                .Select(named => (TableField)new NameKey
                {
                    Key = named.Key,
                    Value = CreateAttributeArgumentExpression(ctx, named.Value),
                }));
        }

        fields.Add(new NoKey
        {
            Expression = new TableConstructor { Fields = positionalArguments, PadEntries = true },
        });

        if (namedFields.Count > 0)
        {
            fields.Add(new NameKey
            {
                Key = "named",
                Value = new TableConstructor { Fields = namedFields, PadEntries = true },
            });
        }

        return new TableConstructor { Fields = fields, PadEntries = true };
    }

    private static Expression CreateAttributeArgumentExpression(TranspilationContext ctx, TypedConstant constant)
    {
        if (constant.Kind == TypedConstantKind.Array)
        {
            var arrayFields = constant.Values
                .Select(value => (TableField)new NoKey { Expression = CreateAttributeArgumentExpression(ctx, value) })
                .ToList();

            return new TableConstructor { Fields = arrayFields };
        }

        if (constant.Kind == TypedConstantKind.Type && constant.Value is ITypeSymbol typeSymbol)
        {
            string typeName;
            if (typeSymbol is INamedTypeSymbol namedTypeSymbol)
            {
                typeName = ctx.GetTypeName(namedTypeSymbol);
                ctx.EnsureTypePredeclaration(typeName);
            }
            else
            {
                typeName = typeSymbol.Name;
            }

            return SymbolExpression.FromString(typeName);
        }

        var value = constant.Value;
        if (value is null)
        {
            return SymbolExpression.FromString("nil");
        }

        return value switch
        {
            string s => StringExpression.FromString(s),
            char c => StringExpression.FromString(c.ToString()),
            bool b => SymbolExpression.FromString(b ? "true" : "false"),
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
                => NumberExpression.From(Convert.ToDouble(value)),
            Enum => StringExpression.FromString(value.ToString() ?? string.Empty),
            _ => StringExpression.FromString(value.ToString() ?? string.Empty),
        };
    }

    private static Expression CreateAttributeArgumentExpressionFromSyntax(AttributeArgumentSyntax argument, TranspilationContext ctx)
        => CreateAttributeArgumentExpressionFromExpression(argument.Expression, ctx);

    private static Expression CreateAttributeArgumentExpressionFromExpression(ExpressionSyntax expression, TranspilationContext ctx)
    {
        var constantValue = ctx.Semantics.GetConstantValue(expression);
        if (constantValue.HasValue)
        {
            var value = constantValue.Value;
            return value switch
            {
                string s => StringExpression.FromString(s),
                char c => StringExpression.FromString(c.ToString()),
                bool b => SymbolExpression.FromString(b ? "true" : "false"),
                sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
                    => NumberExpression.From(Convert.ToDouble(value)),
                _ => StringExpression.FromString(value?.ToString() ?? string.Empty),
            };
        }

        if (expression is LiteralExpressionSyntax literalExpression)
        {
            return literalExpression.Kind() switch
            {
                SyntaxKind.StringLiteralExpression => StringExpression.FromString(literalExpression.Token.ValueText),
                SyntaxKind.CharacterLiteralExpression => StringExpression.FromString(literalExpression.Token.ValueText),
                SyntaxKind.TrueLiteralExpression => SymbolExpression.FromString("true"),
                SyntaxKind.FalseLiteralExpression => SymbolExpression.FromString("false"),
                SyntaxKind.NullLiteralExpression => SymbolExpression.FromString("nil"),
                SyntaxKind.NumericLiteralExpression => NumberExpression.From(Convert.ToDouble(literalExpression.Token.Value ?? 0)),
                _ => ExpressionBuilder.BuildFromSyntax(expression, ctx),
            };
        }

        if (expression is TypeOfExpressionSyntax typeOfExpression)
        {
            var typeSymbol = ctx.Semantics.GetTypeInfo(typeOfExpression.Type).Type as INamedTypeSymbol;
            var typeName = typeSymbol is INamedTypeSymbol namedType
                ? ctx.GetTypeName(namedType)
                : typeOfExpression.Type.ToString();

            if (typeSymbol is INamedTypeSymbol named)
            {
                ctx.EnsureTypePredeclaration(typeName);
            }

            return SymbolExpression.FromString(typeName);
        }

        var expressionText = expression.ToString();

        if (expressionText.StartsWith("\"", StringComparison.Ordinal) && expressionText.EndsWith("\"", StringComparison.Ordinal) && expressionText.Length >= 2)
        {
            return StringExpression.FromString(UnescapeQuotedString(expressionText.Substring(1, expressionText.Length - 2)));
        }

        if (expressionText.StartsWith("'", StringComparison.Ordinal) && expressionText.EndsWith("'", StringComparison.Ordinal) && expressionText.Length >= 2)
        {
            return StringExpression.FromString(UnescapeQuotedString(expressionText.Substring(1, expressionText.Length - 2)));
        }

        if (double.TryParse(expressionText, out var numericValue))
        {
            return NumberExpression.From(numericValue);
        }

        if (string.Equals(expressionText, "true", StringComparison.Ordinal))
        {
            return SymbolExpression.FromString("true");
        }

        if (string.Equals(expressionText, "false", StringComparison.Ordinal))
        {
            return SymbolExpression.FromString("false");
        }

        if (string.Equals(expressionText, "null", StringComparison.Ordinal) || string.Equals(expressionText, "nil", StringComparison.Ordinal))
        {
            return SymbolExpression.FromString("nil");
        }

        return SymbolExpression.FromString(expressionText);
    }

    private static bool IsNamedAttributeArgument(AttributeArgumentSyntax argument)
    {
        if (argument.NameEquals is not null || argument.NameColon is not null)
        {
            return true;
        }

        if (argument.Expression is AssignmentExpressionSyntax)
        {
            return true;
        }

        var text = argument.ToString();
        return text.Contains('=');
    }

    private static string GetAttributeArgumentName(AttributeArgumentSyntax argument)
    {
        if (argument.NameEquals is not null)
        {
            return argument.NameEquals.Name.Identifier.ValueText;
        }

        if (argument.NameColon is not null)
        {
            return argument.NameColon.Name.Identifier.ValueText;
        }

        if (argument.Expression is AssignmentExpressionSyntax assignment && assignment.Left is IdentifierNameSyntax left)
        {
            return left.Identifier.ValueText;
        }

        var text = argument.ToString();
        var equalsIndex = text.IndexOf('=');
        return equalsIndex >= 0 ? text[..equalsIndex].Trim() : text.Trim();
    }

    private static string UnescapeQuotedString(string value)
    {
        return value.Replace("\\\"", "\"").Replace("\\'", "'");
    }

    private static bool TryCreateNamedAttributeField(
        AttributeArgumentSyntax argument,
        TranspilationContext ctx,
        IReadOnlyDictionary<string, TypedConstant> semanticNamedArguments,
        out TableField field)
    {
        field = null!;
        string? name = null;
        ExpressionSyntax? valueSyntax = null;

        if (argument.NameEquals is { } nameEquals)
        {
            name = nameEquals.Name.Identifier.ValueText;
            valueSyntax = argument.Expression;
        }
        else if (argument.NameColon is { } nameColon)
        {
            name = nameColon.Name.Identifier.ValueText;
            valueSyntax = argument.Expression;
        }
        else if (argument.Expression is AssignmentExpressionSyntax assignment && assignment.Left is IdentifierNameSyntax left)
        {
            name = left.Identifier.ValueText;
            valueSyntax = assignment.Right;
        }

        if (name is null || valueSyntax is null)
        {
            return false;
        }

        if (IsMissingAttributeValue(argument, valueSyntax, name))
        {
            return false;
        }

        Expression valueExpression;
        if (!string.IsNullOrEmpty(name) && semanticNamedArguments.TryGetValue(name, out var semanticValue))
        {
            valueExpression = CreateAttributeArgumentExpression(ctx, semanticValue);
        }
        else
        {
            valueExpression = CreateAttributeArgumentExpressionFromExpression(valueSyntax, ctx);
        }

        field = new NameKey
        {
            Key = name,
            Value = valueExpression,
        };

        return true;
    }

    private static bool TryCreateSplitNamedAttributeField(
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments,
        ref int index,
        TranspilationContext ctx,
        IReadOnlyDictionary<string, TypedConstant> semanticNamedArguments,
        out TableField field)
    {
        field = null!;

        if (index >= arguments.Count)
        {
            return false;
        }

        var current = arguments[index];
        if (current.NameEquals is null)
        {
            return false;
        }

        if (IsMissingAttributeValue(current, current.Expression, current.NameEquals.Name.Identifier.ValueText) &&
            index + 1 < arguments.Count)
        {
            var nextArgument = arguments[index + 1];
            if (nextArgument.NameEquals is not null || nextArgument.NameColon is not null)
            {
                return false;
            }

            Expression valueExpression;
            var name = current.NameEquals.Name.Identifier.ValueText;
            if (semanticNamedArguments.TryGetValue(name, out var semanticValue))
            {
                valueExpression = CreateAttributeArgumentExpression(ctx, semanticValue);
            }
            else
            {
                valueExpression = CreateAttributeArgumentExpressionFromExpression(nextArgument.Expression, ctx);
            }
            field = new NameKey
            {
                Key = name,
                Value = valueExpression,
            };

            index++;
            return true;
        }

        return false;
    }

    private static bool IsMissingAttributeValue(AttributeArgumentSyntax argument, ExpressionSyntax? expression, string? expectedName)
    {
        if (expression is null || expression.IsMissing)
        {
            return true;
        }

        if (expression is IdentifierNameSyntax identifier)
        {
            if (identifier.Identifier.IsMissing || string.IsNullOrWhiteSpace(identifier.Identifier.ValueText))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(expectedName) &&
                string.Equals(identifier.Identifier.ValueText, expectedName, StringComparison.Ordinal) &&
                argument.ToString().TrimEnd().EndsWith("=", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static AttributeSyntax? FindAttributeSyntax(AttributeData attribute, TranspilationContext ctx)
    {
        var name = attribute.AttributeClass?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return ctx.Root.DescendantNodes()
            .OfType<AttributeSyntax>()
            .FirstOrDefault(syntax =>
            {
                var syntaxName = syntax.Name.ToString();
                var simpleName = name.EndsWith("Attribute", StringComparison.Ordinal)
                    ? name[..^9]
                    : name;
                return string.Equals(syntaxName, name, StringComparison.Ordinal)
                       || string.Equals(syntaxName, simpleName, StringComparison.Ordinal);
            });
    }
}
