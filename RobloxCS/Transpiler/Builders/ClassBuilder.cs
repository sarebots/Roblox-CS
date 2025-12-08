using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Functions;
using RobloxCS.AST.Prefixes;
using RobloxCS.AST.Parameters;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Suffixes;
using RobloxCS.AST.Types;
using RobloxCS.AST.Generics;
using RobloxCS.Shared;
using TypeInfo = RobloxCS.AST.Types.TypeInfo;
using AttributeData = Microsoft.CodeAnalysis.AttributeData;
using TypedConstant = Microsoft.CodeAnalysis.TypedConstant;

namespace RobloxCS.TranspilerV2.Builders;

internal static class ClassBuilder {
    public static IEnumerable<Statement> Build(ClassDeclarationSyntax node, INamedTypeSymbol classSymbol, TranspilationContext ctx) {
        var className = ctx.EnsureTypePredeclaration(classSymbol);

        Logger.Info($"Transpiling class {className}");

        var instanceMethodSyntaxes = node.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(method => !method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword)))
            .ToList();

        var shouldUseMetatable = instanceMethodSyntaxes.Count > 0;
        var ctorSymbol = classSymbol.InstanceConstructors.FirstOrDefault(ctor => !ctor.IsStatic);

        var predeclarations = new List<Statement>();
        TryAddPredeclaration(predeclarations, ctx, className);
        AddDependencyPredeclarations(predeclarations, ctx, classSymbol);

        var statements = new List<Statement>();
        statements.AddRange(predeclarations);
        statements.Add(DoStatement.FromBlock(BuildClassBody(ctx, node, classSymbol, className, ctorSymbol, shouldUseMetatable)));
        statements.AddRange(CreateInheritanceMetadataStatements(ctx, classSymbol, className));
        statements.Add(CreateDefineGlobalCall(className));
        statements.Add(CreateTypeAlias(ctx, classSymbol, className));
        statements.Add(Return.FromExpressions([SymbolExpression.FromString("nil")]));

        return statements;
    }

    private static void AddDependencyPredeclarations(List<Statement> predeclarations, TranspilationContext ctx, INamedTypeSymbol classSymbol)
    {
        if (classSymbol.BaseType is { SpecialType: not SpecialType.System_Object } baseType)
        {
            TryAddPredeclaration(predeclarations, ctx, ctx.GetTypeName(baseType));
        }

        foreach (var interfaceSymbol in classSymbol.Interfaces)
        {
            TryAddPredeclaration(predeclarations, ctx, ctx.GetTypeName(interfaceSymbol));
        }
    }

    private static void TryAddPredeclaration(List<Statement> predeclarations, TranspilationContext ctx, string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return;
        }

        if (ctx.TryConsumeTypePredeclaration(typeName, out var statement) && statement is not null)
        {
            predeclarations.Add(statement);
        }
    }

    private static Block BuildClassBody(
        TranspilationContext ctx,
        ClassDeclarationSyntax classDeclaration,
        INamedTypeSymbol classSymbol,
        string className,
        IMethodSymbol? ctorSymbol,
        bool shouldUseMetatable
    ) {
        var block = Block.Empty();

        foreach (var nestedType in classDeclaration.Members.OfType<BaseTypeDeclarationSyntax>())
        {
            if (ctx.Semantics.GetDeclaredSymbol(nestedType) is INamedTypeSymbol nestedSymbol)
            {
                ctx.EnsureTypePredeclaration(ctx.GetTypeName(nestedSymbol));
            }
        }

        block.AddStatement(CreateMetatableAssignment(className));
        block.AddStatement(Assignment.AssignToSymbol($"{className}.__index", className));
        block.AddStatement(CreateClassNameAssignment(className));

        if (ctorSymbol is not null)
        {
            block.AddStatement(ConstructorBuilder.CreateConstructorLocal(classSymbol, ctorSymbol, ctx, className));
            block.AddStatement(ConstructorBuilder.CreateNewMethod(classSymbol, ctorSymbol, shouldUseMetatable, className));
        }
        else
        {
            block.AddStatement(ConstructorBuilder.CreateDefaultConstructorLocal(classSymbol));
            block.AddStatement(ConstructorBuilder.CreateDefaultNewMethod(classSymbol, shouldUseMetatable, className));
        }

        foreach (var memberStatement in BuildMemberStatements(ctx, classDeclaration, classSymbol, className)) {
            block.AddStatement(memberStatement);
        }

        return block;
    }

    private static IEnumerable<Statement> BuildMemberStatements(
        TranspilationContext ctx,
        ClassDeclarationSyntax classDeclaration,
        INamedTypeSymbol classSymbol,
        string className
    ) {
        var orderedStatements = new List<(int Index, Statement Statement)>();

        for (var i = 0; i < classDeclaration.Members.Count; i++) {
            var member = classDeclaration.Members[i];

            switch (member) {
                case MethodDeclarationSyntax methodDeclaration: {
                    IMethodSymbol? methodSymbol = null;
                    try
                    {
                        methodSymbol = ctx.Semantics.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
                    }
                    catch (ArgumentException)
                    {
                        // Fallback for synthetic declarations (e.g. struct-to-class lowering) where the syntax node
                        // is not part of the original syntax tree.
                    }

                    methodSymbol ??= classSymbol
                        .GetMembers(methodDeclaration.Identifier.ValueText)
                        .OfType<IMethodSymbol>()
                        .FirstOrDefault(symbol =>
                            symbol.MethodKind == MethodKind.Ordinary &&
                            symbol.Parameters.Length == methodDeclaration.ParameterList?.Parameters.Count &&
                            symbol.IsStatic == methodDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword));

                    if (methodSymbol is null) {
                        continue;
                    }

                    var methodSyntax = methodDeclaration;
                    if (methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is MethodDeclarationSyntax declaredSyntax)
                    {
                        methodSyntax = declaredSyntax;
                    }

                    if (methodSymbol.IsAsync)
                    {
                        ctx.MarkAsync(methodSymbol);
                        RobloxCS.Luau.SymbolMetadataManager.Get(methodSymbol.ContainingType).AsyncMethods.Add(methodSymbol);
                    }

                    var statement = methodDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword)
                        ? FunctionBuilder.CreateStaticMethod(classSymbol, methodSymbol, methodSyntax, ctx, className)
                        : FunctionBuilder.CreateInstanceMethod(classSymbol, methodSymbol, methodSyntax, ctx, className);

                    orderedStatements.Add((i, statement));
                    break;
                }

                case FieldDeclarationSyntax fieldDeclaration when fieldDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword): {
                    foreach (var assignment in StaticMemberBuilder.CreateStaticFieldAssignments(classSymbol, fieldDeclaration, ctx, className)) {
                        orderedStatements.Add((i, assignment));
                    }

                    break;
                }

                case ConstructorDeclarationSyntax constructorDeclaration when constructorDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword): {
                    var ctorSymbol = classSymbol.StaticConstructors.FirstOrDefault();
                    var ctorSyntax = ctorSymbol?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as ConstructorDeclarationSyntax;
                    if (ctorSymbol is null || ctorSyntax is null)
                    {
                        continue;
                    }

                    foreach (var statement in BuildStaticConstructorStatements(ctx, ctorSyntax, classSymbol, className))
                    {
                        orderedStatements.Add((i, statement));
                    }

                    break;
                }

                case PropertyDeclarationSyntax propertyDeclaration when propertyDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword): {
                    if (StaticMemberBuilder.TryCreateStaticPropertyAssignment(classSymbol, propertyDeclaration, ctx, className, out var assignment)) {
                        orderedStatements.Add((i, assignment));
                    }

                    break;
                }

                case PropertyDeclarationSyntax:
                    // Instance property accessors are no longer emitted as standalone Luau functions.
                    break;
            }
        }

        return orderedStatements
            .OrderBy(tuple => tuple.Index)
            .Select(tuple => tuple.Statement);
    }

    private static Assignment CreateMetatableAssignment(string className) {
        var toStringBlock = Block.Empty();
        toStringBlock.AddStatement(Return.FromExpressions([StringExpression.FromString(className)]));

        var toStringFunction = new AnonymousFunction {
            Body = new FunctionBody {
                Parameters = [],
                TypeSpecifiers = [],
                ReturnType = BasicTypeInfo.String(),
                Body = toStringBlock,
            },
        };

        var metaTable = TableConstructor.With(new List<TableField> {
            new NameKey { Key = "__tostring", Value = toStringFunction },
        });

        return new Assignment {
            Vars = [VarName.FromString(className)],
            Expressions = [FunctionCall.Basic("setmetatable", TableConstructor.Empty(), metaTable)],
        };
    }

    private static IEnumerable<Statement> BuildStaticConstructorStatements(
        TranspilationContext ctx,
        ConstructorDeclarationSyntax constructorDeclaration,
        INamedTypeSymbol classSymbol,
        string className)
    {
        if (constructorDeclaration.Body is not { } body)
        {
            yield break;
        }

        if (StaticCtorUsesExternalSignal(body, classSymbol, ctx))
        {
            ctx.EnsureSignalImport();
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

        var staticFields = classSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.IsStatic)
            .Select(f => f.Name)
            .ToHashSet(StringComparer.Ordinal);

        var staticProperties = classSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.IsStatic)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var staticEvents = classSymbol.GetMembers()
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
                        assignment.Vars[i] = VarName.FromString($"{className}.{name}");
                    }
                }
            }

            yield return statement;
        }
    }

    private static bool StaticCtorUsesExternalSignal(BlockSyntax body, INamedTypeSymbol classSymbol, TranspilationContext ctx)
    {
        foreach (var assignment in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Kind() is not SyntaxKind.SimpleAssignmentExpression)
            {
                continue;
            }

            var symbolInfo = ctx.Semantics.GetSymbolInfo(assignment.Left);
            var eventSymbol = symbolInfo.Symbol as IEventSymbol
                ?? symbolInfo.CandidateSymbols.OfType<IEventSymbol>().FirstOrDefault()
                ?? TryMatchEventByName(classSymbol, assignment.Left);

            if (eventSymbol is not { IsStatic: true })
            {
                continue;
            }

            if (UsesExternalSignal(assignment.Right, classSymbol, ctx))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UsesExternalSignal(ExpressionSyntax expression, INamedTypeSymbol classSymbol, TranspilationContext ctx)
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
            !SymbolEqualityComparer.Default.Equals(signalMethodSymbol.ContainingType?.ContainingType, classSymbol))
        {
            return true;
        }

        if (expression is InvocationExpressionSyntax invocation)
        {
            return ReferencesSignal(invocation.Expression, classSymbol, ctx);
        }

        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            return ReferencesSignal(memberAccess.Expression, classSymbol, ctx);
        }

        return expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
            .Any(identifier => ReferencesSignal(identifier, classSymbol, ctx));
    }

    private static bool ReferencesSignal(ExpressionSyntax expression, INamedTypeSymbol classSymbol, TranspilationContext ctx)
    {
        if (expression is IdentifierNameSyntax identifier &&
            string.Equals(identifier.Identifier.ValueText, "Signal", StringComparison.Ordinal))
        {
            var symbol = ctx.Semantics.GetSymbolInfo(identifier).Symbol;
            if (symbol is ITypeSymbol typeSymbol &&
                (SymbolEqualityComparer.Default.Equals(typeSymbol, classSymbol)
                 || SymbolEqualityComparer.Default.Equals(typeSymbol.ContainingType, classSymbol)))
            {
                return false;
            }

            if (symbol is IMethodSymbol methodSymbol &&
                SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType?.ContainingType, classSymbol))
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
                (SymbolEqualityComparer.Default.Equals(typeSymbol, classSymbol)
                 || SymbolEqualityComparer.Default.Equals(typeSymbol.ContainingType, classSymbol)))
            {
                return false;
            }

            if (symbol is IMethodSymbol methodSymbol &&
                SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType?.ContainingType, classSymbol))
            {
                return false;
            }

            return true;
        }

        return expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
            .Any(identifier => string.Equals(identifier.Identifier.ValueText, "Signal", StringComparison.Ordinal));
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

    private static IEventSymbol? TryMatchEventByName(INamedTypeSymbol classSymbol, ExpressionSyntax left)
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

        return classSymbol.GetMembers()
            .OfType<IEventSymbol>()
            .FirstOrDefault(evt => evt.IsStatic && string.Equals(evt.Name, name, StringComparison.Ordinal));
    }

    private static Assignment CreateClassNameAssignment(string className) {
        return new Assignment {
            Vars = [VarName.FromString($"{className}.__className")],
            Expressions = [StringExpression.FromString(className)],
        };
    }

    private static Statement CreateDefineGlobalCall(string className) {
        return new FunctionCallStatement {
            Prefix = NamePrefix.FromString("CS.defineGlobal"),
            Suffixes = [
                new AnonymousCall {
                    Arguments = new FunctionArgs {
                        Arguments = [StringExpression.FromString(className), SymbolExpression.FromString(className)],
                    },
                },
            ],
        };
    }

    private static Statement CreateTypeAlias(TranspilationContext ctx, INamedTypeSymbol classSymbol, string className) {
        var typeDeclaration = new TypeDeclaration {
            Name = className,
            DeclareAs = new TypeOfTypeInfo {
                Expression = SymbolExpression.FromString(className),
            },
        };

        if (classSymbol.TypeParameters.Length > 0)
        {
            typeDeclaration.Declarations = [CreateGenericDeclaration(ctx, classSymbol.TypeParameters)];
        }

        return typeDeclaration;
    }

    private static IEnumerable<Statement> CreateInheritanceMetadataStatements(TranspilationContext ctx, INamedTypeSymbol classSymbol, string className)
    {
        var statements = new List<Statement>();

        if (classSymbol.BaseType is { SpecialType: not SpecialType.System_Object } baseType)
        {
            var baseName = ctx.GetTypeName(baseType);
            ctx.EnsureTypePredeclaration(baseName);
            statements.Add(Assignment.AssignToSymbol($"{className}.__base", baseName));
        }

        var interfaceFields = BuildInterfaceFields(ctx, classSymbol.Interfaces);

        if (interfaceFields.Count > 0)
        {
            statements.Add(new Assignment
            {
                Vars = [VarName.FromString($"{className}.__interfaces")],
                Expressions = [new TableConstructor { Fields = interfaceFields, PadEntries = true }],
            });
        }

        var attributeFields = BuildAttributeFields(ctx, classSymbol.GetAttributes());

        if (attributeFields.Count > 0)
        {
            statements.Add(new Assignment
            {
                Vars = [VarName.FromString($"{className}.__attributes")],
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
