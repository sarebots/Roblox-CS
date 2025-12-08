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
using FunctionCallAst = RobloxCS.AST.Expressions.FunctionCall;

namespace RobloxCS.TranspilerV2.Builders;

internal static class FunctionBuilder {
    public static Statement CreateInstanceMethod(
        INamedTypeSymbol classSymbol,
        IMethodSymbol methodSymbol,
        MethodDeclarationSyntax methodSyntax,
        TranspilationContext ctx,
        string className
    ) {
        var isAsync = ctx.IsAsync(methodSymbol);
        var parameters = new List<Parameter>();
        var typeSpecifiers = new List<TypeInfo>();

        if (isAsync)
        {
            parameters.Add(NameParameter.FromString("self"));
            typeSpecifiers.Add(SyntaxUtilities.BasicFromSymbol(classSymbol));
        }

        foreach (var parameter in methodSymbol.Parameters.Where(p => !p.IsImplicitlyDeclared)) {
            parameters.Add(NameParameter.FromString(parameter.Name));
            typeSpecifiers.Add(CreateParameterType(parameter));
        }

        var bodyBlock = BuildMethodBody(methodSymbol, methodSyntax.Body, methodSyntax.ExpressionBody, ctx);

        var returnType = methodSymbol.ReturnsVoid
            ? BasicTypeInfo.Void()
            : SyntaxUtilities.BasicFromSymbol(methodSymbol.ReturnType);

        GeneratorTransformer.TryConvertGenerator(ctx, methodSymbol, methodSyntax.Body, bodyBlock, ref returnType);

        if (isAsync)
        {
            var functionBody = new FunctionBody
            {
                Parameters = parameters,
                TypeSpecifiers = typeSpecifiers,
                ReturnType = returnType,
                Body = bodyBlock,
            };

            var asyncFunction = new AnonymousFunction { Body = functionBody };
            var asyncCall = FunctionCallAst.Basic("CS.async", asyncFunction);

            return new Assignment
            {
                Vars = [VarName.FromString($"{className}.{methodSymbol.Name}")],
                Expressions = [asyncCall],
            };
        }

        return new FunctionDeclaration {
            Name = FunctionName.FromString($"{className}:{methodSymbol.Name}"),
            Body = new FunctionBody {
                Parameters = parameters,
                TypeSpecifiers = typeSpecifiers,
                ReturnType = returnType,
                Body = bodyBlock,
            },
        };
    }

    public static Statement CreateStaticMethod(
        INamedTypeSymbol classSymbol,
        IMethodSymbol methodSymbol,
        MethodDeclarationSyntax methodSyntax,
        TranspilationContext ctx,
        string className
    ) {
        var isAsync = ctx.IsAsync(methodSymbol);
        var parameters = new List<Parameter>();
        var typeSpecifiers = new List<TypeInfo>();

        foreach (var parameter in methodSymbol.Parameters.Where(p => !p.IsImplicitlyDeclared)) {
            parameters.Add(NameParameter.FromString(parameter.Name));
            typeSpecifiers.Add(CreateParameterType(parameter));
        }

        var bodyBlock = BuildMethodBody(methodSymbol, methodSyntax.Body, methodSyntax.ExpressionBody, ctx);

        var returnType = methodSymbol.ReturnsVoid
            ? BasicTypeInfo.Void()
            : SyntaxUtilities.BasicFromSymbol(methodSymbol.ReturnType);

        GeneratorTransformer.TryConvertGenerator(ctx, methodSymbol, methodSyntax.Body, bodyBlock, ref returnType);

        if (isAsync)
        {
            var functionBody = new FunctionBody
            {
                Parameters = parameters,
                TypeSpecifiers = typeSpecifiers,
                ReturnType = returnType,
                Body = bodyBlock,
            };

            var asyncFunction = new AnonymousFunction { Body = functionBody };
            var asyncCall = FunctionCallAst.Basic("CS.async", asyncFunction);

            return new Assignment
            {
                Vars = [VarName.FromString($"{className}.{methodSymbol.Name}")],
                Expressions = [asyncCall],
            };
        }

        return new FunctionDeclaration {
            Name = FunctionName.FromString($"{className}.{methodSymbol.Name}"),
            Body = new FunctionBody {
                Parameters = parameters,
                TypeSpecifiers = typeSpecifiers,
                ReturnType = returnType,
                Body = bodyBlock,
            },
        };
    }

    internal static Block BuildMethodBody(
        IMethodSymbol methodSymbol,
        BlockSyntax? bodySyntax,
        ArrowExpressionClauseSyntax? expressionBodySyntax,
        TranspilationContext ctx
    ) {
        var block = Block.Empty();

        if (bodySyntax is { } body) {
            if (!ContainsYieldStatements(body))
            {
                ctx.PushScope();

                foreach (var statementSyntax in body.Statements)
                {
                    var statement = StatementBuilder.Transpile(statementSyntax, ctx);
                    ctx.AppendPrerequisites(block);
                    block.AddStatement(statement);
                }

                ctx.AppendPrerequisites(block);
                ctx.PopScope();
            }

            return block;
        }

        if (expressionBodySyntax is { } expressionBody) {
            ctx.PushScope();
            var expression = ExpressionBuilder.BuildFromSyntax(expressionBody.Expression, ctx);
            ctx.PopScope();

            ctx.AppendPrerequisites(block);

            if (methodSymbol.ReturnsVoid) {
                if (expression is FunctionCall callExpression) {
                    block.AddStatement(new FunctionCallStatement {
                        Prefix = callExpression.Prefix,
                        Suffixes = callExpression.Suffixes,
                    });
                } else {
                    throw new NotSupportedException("Unsupported expression-bodied void method.");
                }
            } else {
                block.AddStatement(Return.FromExpressions([expression]));
            }

            return block;
        }

        return block;
    }

    private static bool ContainsYieldStatements(BlockSyntax bodySyntax)
    {
        foreach (var node in bodySyntax.DescendantNodesAndSelf())
        {
            if (node.IsKind(SyntaxKind.YieldBreakStatement) || node.IsKind(SyntaxKind.YieldReturnStatement))
            {
                return true;
            }
        }

        return false;
    }

    internal static FunctionBody CreateAnonymousFunctionBody(
        IMethodSymbol methodSymbol,
        BlockSyntax? bodySyntax,
        ArrowExpressionClauseSyntax? expressionBodySyntax,
        TranspilationContext ctx
    ) {
        var parameters = new List<Parameter>();
        var typeSpecifiers = new List<TypeInfo>();

        foreach (var parameter in methodSymbol.Parameters.Where(p => !p.IsImplicitlyDeclared)) {
            parameters.Add(NameParameter.FromString(parameter.Name));
            typeSpecifiers.Add(CreateParameterType(parameter));
        }

        var returnType = methodSymbol.ReturnsVoid
            ? BasicTypeInfo.Void()
            : SyntaxUtilities.BasicFromSymbol(methodSymbol.ReturnType);

        var bodyBlock = BuildMethodBody(methodSymbol, bodySyntax, expressionBodySyntax, ctx);

        GeneratorTransformer.TryConvertGenerator(ctx, methodSymbol, bodySyntax, bodyBlock, ref returnType);

        return new FunctionBody {
            Parameters = parameters,
            TypeSpecifiers = typeSpecifiers,
            ReturnType = returnType,
            Body = bodyBlock,
        };
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

        if (parameter.IsParams)
        {
            if (baseType is ArrayTypeInfo arrayType)
            {
                baseType = new VariadicTypeInfo { Inner = arrayType.ElementType };
            }
            else
            {
                baseType = new VariadicTypeInfo { Inner = baseType };
            }
        }

        if (parameter.HasExplicitDefaultValue)
        {
            baseType = new OptionalTypeInfo { Inner = baseType };
        }

        return baseType;
    }
}
