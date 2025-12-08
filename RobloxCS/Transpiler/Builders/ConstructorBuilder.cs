using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Functions;
using RobloxCS.AST.Parameters;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Types;
using LuauAstUtility = RobloxCS.Luau.AstUtility;
using TypeInfo = RobloxCS.AST.Types.TypeInfo;

namespace RobloxCS.TranspilerV2.Builders;

internal static class ConstructorBuilder
{
    public static FunctionDeclaration CreateNewMethod(INamedTypeSymbol classSymbol, IMethodSymbol ctorSymbol, bool useMetatable, string className)
    {
        var parameters = new List<Parameter> { NameParameter.FromString("self") };
        var typeSpecifiers = new List<TypeInfo> { SyntaxUtilities.BasicFromSymbol(classSymbol) };
        var callArgs = new List<Expression> { SymbolExpression.FromString("self") };

        var includeParameters = !ctorSymbol.IsImplicitlyDeclared || classSymbol.IsRecord;

        if (includeParameters)
        {
            foreach (var parameter in ctorSymbol.Parameters)
            {
                parameters.Add(NameParameter.FromString(parameter.Name));
                typeSpecifiers.Add(CreateParameterType(parameter));
                callArgs.Add(SymbolExpression.FromString(parameter.Name));
            }
        }

        var block = Block.Empty();
        block.AddStatement(new LocalAssignment
        {
            Names = [SymbolExpression.FromString("self")],
            Expressions = [CreateSelfInitializer(classSymbol, useMetatable, className)],
            Types = [],
        });

        var constructorCall = FunctionCall.Basic("constructor", callArgs.ToArray());
        block.AddStatement(new Return
        {
            Returns =
            [
                new BinaryOperatorExpression
                {
                    Left = constructorCall,
                    Right = SymbolExpression.FromString("self"),
                    Op = BinOp.Or,
                },
            ],
        });

        return new FunctionDeclaration
        {
            Name = FunctionName.FromString($"{className}.new"),
            Body = new FunctionBody
            {
                Parameters = parameters,
                TypeSpecifiers = typeSpecifiers,
                ReturnType = SyntaxUtilities.BasicFromSymbol(classSymbol),
                Body = block,
            },
        };
    }

    public static LocalAssignment CreateDefaultConstructorLocal(INamedTypeSymbol classSymbol)
    {
        var parameters = new List<Parameter> { NameParameter.FromString("self") };
        var typeSpecifiers = new List<TypeInfo> { SyntaxUtilities.BasicFromSymbol(classSymbol) };

        var block = Block.Empty();
        block.AddStatement(Return.FromExpressions([SymbolExpression.FromString("nil")]));

        return new LocalAssignment
        {
            Names = [SymbolExpression.FromString("constructor")],
            Expressions =
            [
                new AnonymousFunction
                {
                    Body = new FunctionBody
                    {
                        Parameters = parameters,
                        TypeSpecifiers = typeSpecifiers,
                        ReturnType = new OptionalTypeInfo { Inner = SyntaxUtilities.BasicFromSymbol(classSymbol) },
                        Body = block,
                    },
                },
            ],
            Types = [],
        };
    }

    public static FunctionDeclaration CreateDefaultNewMethod(INamedTypeSymbol classSymbol, bool useMetatable, string className)
    {
        var parameters = new List<Parameter> { NameParameter.FromString("self") };
        var typeSpecifiers = new List<TypeInfo> { SyntaxUtilities.BasicFromSymbol(classSymbol) };

        var block = Block.Empty();
        block.AddStatement(new LocalAssignment
        {
            Names = [SymbolExpression.FromString("self")],
            Expressions = [CreateSelfInitializer(classSymbol, useMetatable, className)],
            Types = [],
        });

        var constructorCall = FunctionCall.Basic("constructor", SymbolExpression.FromString("self"));
        block.AddStatement(new Return
        {
            Returns =
            [
                new BinaryOperatorExpression
                {
                    Left = constructorCall,
                    Right = SymbolExpression.FromString("self"),
                    Op = BinOp.Or,
                },
            ],
        });

        return new FunctionDeclaration
        {
            Name = FunctionName.FromString($"{className}.new"),
            Body = new FunctionBody
            {
                Parameters = parameters,
                TypeSpecifiers = typeSpecifiers,
                ReturnType = SyntaxUtilities.BasicFromSymbol(classSymbol),
                Body = block,
            },
        };
    }

    public static LocalAssignment CreateConstructorLocal(
        INamedTypeSymbol classSymbol,
        IMethodSymbol ctorSymbol,
        TranspilationContext ctx,
        string className)
    {
        var parameters = new List<Parameter> { NameParameter.FromString("self") };
        var typeSpecifiers = new List<TypeInfo> { SyntaxUtilities.BasicFromSymbol(classSymbol) };

        if (!ctorSymbol.IsImplicitlyDeclared)
        {
            foreach (var parameter in ctorSymbol.Parameters)
            {
                parameters.Add(NameParameter.FromString(parameter.Name));
                typeSpecifiers.Add(CreateParameterType(parameter));
            }
        }

        var block = Block.Empty();
        var instanceProperties = classSymbol.GetMembers().OfType<IPropertySymbol>().Where(property => !property.IsStatic);
        foreach (var assignment in PropertyBuilder.CreatePropertyAssignmentsFromProperties(instanceProperties, ctx))
        {
            ctx.AppendPrerequisites(block);
            block.AddStatement(assignment);
        }

        var instanceFields = classSymbol.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic);
        foreach (var assignment in FieldBuilder.CreateFieldAssignmentsFromFields(instanceFields, ctx))
        {
            ctx.AppendPrerequisites(block);
            block.AddStatement(assignment);
        }

        var instanceEvents = classSymbol.GetMembers().OfType<IEventSymbol>().Where(@event => !@event.IsStatic).ToList();
        if (instanceEvents.Count > 0)
        {
            ctx.EnsureSignalImport();
        }

        foreach (var eventSymbol in instanceEvents)
        {
            block.AddStatement(new Assignment
            {
                Vars = [VarName.FromString($"self.{eventSymbol.Name}")],
                Expressions = [FunctionCall.Basic("Signal.new")],
            });
        }

        ConstructorDeclarationSyntax? ctorSyntax = null;

        if (!ctorSymbol.IsImplicitlyDeclared)
        {
            ctorSyntax = SyntaxUtilities.GetSyntaxFromSymbol<ConstructorDeclarationSyntax>(ctorSymbol);
        }

        var ctorBodyStatements = ctorSyntax?.Body?.Statements ?? default;

        if (ctorBodyStatements is { Count: > 0 })
        {
            ctx.PushScope();

            foreach (var statementSyntax in ctorBodyStatements)
            {
                var statement = StatementBuilder.Transpile(statementSyntax, ctx);
                ctx.AppendPrerequisites(block);
                block.AddStatement(statement);
            }

            ctx.AppendPrerequisites(block);

            ctx.PopScope();
        }

        if (classSymbol.IsRecord && ctorSymbol.Parameters.Length > 0)
        {
            foreach (var parameter in ctorSymbol.Parameters)
            {
                var property = classSymbol.GetMembers()
                    .OfType<IPropertySymbol>()
                    .FirstOrDefault(prop => string.Equals(prop.Name, parameter.Name, StringComparison.Ordinal));

                var targetName = property?.Name ?? parameter.Name;
                block.AddStatement(new Assignment
                {
                    Vars = [VarName.FromString($"self.{targetName}")],
                    Expressions = [SymbolExpression.FromString(parameter.Name)],
                });
            }
        }

        block.AddStatement(Return.FromExpressions([SymbolExpression.FromString("nil")]));

        var functionBody = new FunctionBody
        {
            Parameters = parameters,
            TypeSpecifiers = typeSpecifiers,
            ReturnType = new OptionalTypeInfo { Inner = SyntaxUtilities.BasicFromSymbol(classSymbol) },
            Body = block,
        };

        var anonymousCtor = new AnonymousFunction { Body = functionBody };

        return new LocalAssignment
        {
            Names = [SymbolExpression.FromString("constructor")],
            Expressions = [anonymousCtor],
            Types = [],
        };
    }

    internal static Expression CreateSelfInitializer(INamedTypeSymbol classSymbol, bool useMetatable, string className)
    {
        var targetType = SyntaxUtilities.BasicFromSymbol(classSymbol);

        if (!useMetatable)
        {
            return TypeAssertionExpression.From(TableConstructor.Empty(), targetType);
        }

        var setMetatable = FunctionCall.Basic(
            "setmetatable",
            TableConstructor.Empty(),
            SymbolExpression.FromString(className)
        );

        var anyType = BasicTypeInfo.FromString("any");
        var innerAssertion = TypeAssertionExpression.From(setMetatable, anyType);

        return TypeAssertionExpression.From(innerAssertion, targetType);
    }

    private static TypeInfo CreateParameterType(IParameterSymbol parameter)
    {
        var baseType = SyntaxUtilities.BasicFromSymbol(parameter.Type);

        if (parameter.RefKind is RefKind.Ref or RefKind.Out)
        {
            return new CallbackTypeInfo
            {
                Arguments =
                [
                    new TypeArgument
                    {
                        TypeInfo = new OptionalTypeInfo { Inner = baseType },
                    },
                ],
                ReturnType = baseType,
            };
        }

        return parameter.HasExplicitDefaultValue ? new OptionalTypeInfo { Inner = baseType } : baseType;
    }
}
