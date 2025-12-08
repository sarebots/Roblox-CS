using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Generics;
using RobloxCS.AST.Prefixes;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Suffixes;
using RobloxCS.AST.Types;
using RobloxCS.Shared;
using TypeInfo = RobloxCS.AST.Types.TypeInfo;

namespace RobloxCS.TranspilerV2.Builders.Declarations;

internal static class InterfaceDeclarationBuilder
{
    internal sealed record InterfaceMetadata(
        string Name,
        GenericDeclaration? Generics,
        IReadOnlyList<TypeInfo> BaseInterfaces,
        IReadOnlyList<InterfaceMember> InstanceMembers,
        IReadOnlyList<InterfaceMember> StaticMembers);

    internal sealed record InterfaceMember(
        string Name,
        InterfaceMemberKind Kind,
        IMethodSymbol? MethodSymbol,
        IPropertySymbol? PropertySymbol,
        IEventSymbol? EventSymbol,
        TypeInfo? ReturnType,
        IReadOnlyList<IParameterSymbol> Parameters,
        bool IsStatic);

    internal enum InterfaceMemberKind
    {
        Method,
        Property,
        Event,
    }

    public static InterfaceMetadata Analyze(InterfaceDeclarationSyntax node, INamedTypeSymbol interfaceSymbol, TranspilationContext ctx)
    {
        var name = ctx.GetTypeName(interfaceSymbol);
        var generics = interfaceSymbol.TypeParameters.Length > 0
            ? CreateGenericDeclaration(ctx, interfaceSymbol.TypeParameters)
            : null;
        var baseInterfaces = interfaceSymbol.Interfaces.Select(SyntaxUtilities.TypeInfoFromSymbol).ToList();

        var membersByName = new Dictionary<string, InterfaceMember>(StringComparer.Ordinal);
        var staticMembersByName = new Dictionary<string, InterfaceMember>(StringComparer.Ordinal);

        void AddOrReplaceMember(ISymbol symbol, InterfaceMember member)
        {
            var target = member.IsStatic ? staticMembersByName : membersByName;
            target[symbol.Name] = member;
        }

        AddMembers(interfaceSymbol, AddOrReplaceMember);

        foreach (var inheritedInterface in interfaceSymbol.AllInterfaces)
        {
            AddMembers(inheritedInterface, (symbol, member) =>
            {
                var target = member.IsStatic ? staticMembersByName : membersByName;
                if (!target.ContainsKey(symbol.Name))
                {
                    AddOrReplaceMember(symbol, member);
                }
            });
        }

        return new InterfaceMetadata(
            name,
            generics,
            baseInterfaces,
            membersByName.Values.ToList(),
            staticMembersByName.Values.ToList());

        void AddMembers(INamedTypeSymbol source, Action<ISymbol, InterfaceMember> addMember)
        {
            foreach (var member in source.GetMembers())
            {
                var isStatic = member.IsStatic;
                switch (member)
                {
                    case IMethodSymbol methodSymbol when methodSymbol.MethodKind == MethodKind.Ordinary:
                    {
                        addMember(methodSymbol, new InterfaceMember(
                            methodSymbol.Name,
                            InterfaceMemberKind.Method,
                            methodSymbol,
                            null,
                            null,
                            SyntaxUtilities.TypeInfoFromSymbol(methodSymbol.ReturnType),
                            methodSymbol.Parameters,
                            isStatic));
                        break;
                    }

                    case IPropertySymbol propertySymbol:
                    {
                        addMember(propertySymbol, new InterfaceMember(
                            propertySymbol.Name,
                            InterfaceMemberKind.Property,
                            null,
                            propertySymbol,
                            null,
                            SyntaxUtilities.TypeInfoFromSymbol(propertySymbol.Type),
                            propertySymbol.Parameters,
                            isStatic));
                        break;
                    }

                    case IEventSymbol eventSymbol:
                    {
                        addMember(eventSymbol, new InterfaceMember(
                            eventSymbol.Name,
                            InterfaceMemberKind.Event,
                            null,
                            null,
                            eventSymbol,
                            SyntaxUtilities.TypeInfoFromSymbol(eventSymbol.Type),
                            Array.Empty<IParameterSymbol>(),
                            isStatic));
                        break;
                    }
                }
            }
        }
    }

    public static Statement CreatePlaceholderComment(InterfaceMetadata metadata)
    {
        var comment = $"-- roblox-cs TODO: interface '{metadata.Name}' is not supported yet.";
        return new FunctionCallStatement
        {
            Prefix = NamePrefix.FromString(comment),
            Suffixes = new List<Suffix>(),
        };
    }

    public static bool TryBuildInterfaceAlias(InterfaceMetadata metadata, out List<Statement> statements)
    {
        statements = new List<Statement>();

        BuildAlias(metadata.Name, metadata.InstanceMembers, metadata, statements);
        if (metadata.StaticMembers.Count > 0)
        {
            BuildAlias($"{metadata.Name}_static", metadata.StaticMembers, metadata, statements);
        }

        statements.Add(Return.FromExpressions([SymbolExpression.FromString("nil")]));
        return true;
    }

    private static void BuildAlias(string name, IEnumerable<InterfaceMember> members, InterfaceMetadata metadata, List<Statement> statements)
    {
        var supportedMembers = members
            .Where(SupportsMember)
            .ToList();

        var tableType = new TableTypeInfo
        {
            Fields = supportedMembers
                .Select(CreateTypeField)
                .ToList(),
        };

        var declaration = new TypeDeclaration
        {
            Name = name,
            DeclareAs = tableType,
        };

        if (metadata.Generics is not null)
        {
            declaration.Declarations = [metadata.Generics];
        }

        statements.Add(declaration);
    }

    private static bool SupportsMember(InterfaceMember member)
    {
        return member.Kind switch
        {
            InterfaceMemberKind.Property => true,
            InterfaceMemberKind.Method => true,
            InterfaceMemberKind.Event => true,
            _ => false,
        };
    }

    private static TypeField CreateTypeField(InterfaceMember member) => member.Kind switch
    {
        InterfaceMemberKind.Property => CreatePropertyField(member),
        InterfaceMemberKind.Method => CreateMethodField(member),
        InterfaceMemberKind.Event => CreateEventField(member),
        _ => TypeField.FromNameAndType(member.Name, BasicTypeInfo.FromString("any")),
    };

    private static TypeField CreatePropertyField(InterfaceMember member)
    {
        if (member.PropertySymbol is { Parameters.Length: > 0 } propertyWithParameters)
        {
            var callback = new CallbackTypeInfo
            {
                Arguments = propertyWithParameters.Parameters
                    .Select(parameter => new TypeArgument
                    {
                        TypeInfo = SyntaxUtilities.TypeInfoFromSymbol(parameter.Type),
                    })
                    .ToList(),
                ReturnType = member.ReturnType ?? BasicTypeInfo.FromString("any"),
            };

            return TypeField.FromNameAndType(member.Name, callback);
        }

        var typeInfo = member.ReturnType ?? BasicTypeInfo.FromString("any");
        var field = TypeField.FromNameAndType(member.Name, typeInfo);

        if (member.PropertySymbol is { GetMethod: not null, SetMethod: null })
        {
            field.Access = AccessModifier.Read;
        }
        else if (member.PropertySymbol is { GetMethod: null, SetMethod: not null })
        {
            field.Access = AccessModifier.Write;
        }

        return field;
    }

    private static TypeField CreateMethodField(InterfaceMember member)
    {
        var methodSymbol = member.MethodSymbol!;
        var callback = new CallbackTypeInfo
        {
            Arguments = member.Parameters
                .Select(parameter => new TypeArgument
                {
                    TypeInfo = SyntaxUtilities.TypeInfoFromSymbol(parameter.Type),
                })
                .ToList(),
            ReturnType = member.ReturnType ?? BasicTypeInfo.Void(),
        };

        if (methodSymbol.TypeParameters.Length > 0)
        {
            callback.Generics = new GenericDeclaration
            {
                Parameters = methodSymbol.TypeParameters
                    .Select(typeParameter => new GenericDeclarationParameter
                    {
                        Parameter = NameGenericParameter.FromString(typeParameter.Name),
                    })
                    .ToList(),
            };
        }

        return TypeField.FromNameAndType(member.Name, callback);
    }

    private static TypeField CreateEventField(InterfaceMember member)
    {
        return TypeField.FromNameAndType(member.Name, BasicTypeInfo.FromString("Signal"));
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
}
