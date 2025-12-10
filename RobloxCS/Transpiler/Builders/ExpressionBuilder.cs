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
using RobloxCS.Renderer;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2.Nodes;
using SharedConstants = RobloxCS.Shared.Constants;

namespace RobloxCS.TranspilerV2.Builders;

using FunctionCallAst = RobloxCS.AST.Expressions.FunctionCall;

public class ExpressionBuilder {
    public static Expression BuildFromExpressionStatement(ExpressionStatementSyntax exprStmtSyntax) {
        throw new NotImplementedException();
    }

    public static Expression BuildFromSyntax(ExpressionSyntax syntax, TranspilationContext ctx) =>
        BuildFromSyntaxInternal(syntax, ctx, allowTransformers: true);

    private static Expression BuildFromSyntaxInternal(ExpressionSyntax syntax, TranspilationContext ctx, bool allowTransformers) {
        if (allowTransformers && TransformerRegistry.TryGetExpressionTransformer(syntax.Kind(), out var transformer) && transformer is not null)
        {
            var context = new TransformContext(
                ctx,
                expressionSyntax => BuildFromSyntaxInternal(expressionSyntax, ctx, allowTransformers: true),
                expressionSyntax => BuildFromSyntaxInternal(expressionSyntax, ctx, allowTransformers: false));

            var transformed = transformer(context, syntax);
            if (transformed is not null)
            {
                return transformed;
            }
        }

        return syntax switch {
            IdentifierNameSyntax nameSyntax => HandleIdentifierNameSyntax(nameSyntax, ctx),
            LiteralExpressionSyntax exprSyntax => HandleLiteralExpressionLegacy(exprSyntax, ctx),
            BinaryExpressionSyntax binExprSyntax => HandleBinaryExpressionSyntax(binExprSyntax, ctx),
            InvocationExpressionSyntax invocationSyntax when TryBuildInvocationMacroExpression(invocationSyntax, ctx, out var macroExpression) => macroExpression,
            InvocationExpressionSyntax invocationSyntax => BuildFunctionCall(invocationSyntax, ctx),
            AwaitExpressionSyntax awaitSyntax => HandleAwaitExpression(awaitSyntax, ctx),
            ObjectCreationExpressionSyntax objectCreationSyntax => HandleObjectCreationExpressionLegacy(objectCreationSyntax, ctx),
            MemberAccessExpressionSyntax memberAccessSyntax => HandleMemberAccessExpression(memberAccessSyntax, ctx),
            ElementAccessExpressionSyntax elementAccessSyntax => HandleElementAccessExpressionLegacy(elementAccessSyntax, ctx),
            PrefixUnaryExpressionSyntax prefixUnarySyntax => HandlePrefixUnaryExpression(prefixUnarySyntax, ctx),
            ParenthesizedExpressionSyntax parenthesizedExpression => BuildFromSyntax(parenthesizedExpression.Expression, ctx),
            ArrayCreationExpressionSyntax arrayCreationSyntax => HandleArrayCreationExpressionLegacy(arrayCreationSyntax, ctx),
            ImplicitArrayCreationExpressionSyntax implicitArrayCreationSyntax => HandleImplicitArrayCreationExpressionLegacy(implicitArrayCreationSyntax, ctx),
            CollectionExpressionSyntax collectionExpressionSyntax => HandleCollectionExpressionLegacy(collectionExpressionSyntax, ctx),
            ImplicitObjectCreationExpressionSyntax implicitObjectCreationSyntax => HandleImplicitObjectCreationExpressionLegacy(implicitObjectCreationSyntax, ctx),
            ThisExpressionSyntax => SymbolExpression.FromString("self"),
            ParenthesizedLambdaExpressionSyntax lambdaSyntax => HandleParenthesizedLambda(lambdaSyntax, ctx),
            SimpleLambdaExpressionSyntax simpleLambdaSyntax => HandleSimpleLambda(simpleLambdaSyntax, ctx),
            AnonymousMethodExpressionSyntax anonymousMethodSyntax => HandleAnonymousMethod(anonymousMethodSyntax, ctx),
            CastExpressionSyntax castExpression => BuildFromSyntax(castExpression.Expression, ctx),
            ConditionalAccessExpressionSyntax conditionalAccessSyntax => HandleConditionalAccessExpressionLegacy(conditionalAccessSyntax, ctx),
            InterpolatedStringExpressionSyntax interpolatedStringSyntax => HandleInterpolatedStringExpression(interpolatedStringSyntax, ctx),
            ConditionalExpressionSyntax conditionalSyntax => HandleConditionalExpression(conditionalSyntax, ctx),
            TupleExpressionSyntax tupleExpressionSyntax => HandleTupleExpression(tupleExpressionSyntax, ctx),

            _ => throw new NotSupportedException($"Expression {syntax.Kind()} is not supported. {syntax}"),
        };
    }

    private static Expression HandleConditionalExpression(ConditionalExpressionSyntax syntax, TranspilationContext ctx)
    {
        var condition = BuildFromSyntax(syntax.Condition, ctx);
        var whenTrue = BuildFromSyntax(syntax.WhenTrue, ctx);
        var whenFalse = BuildFromSyntax(syntax.WhenFalse, ctx);

        return new IfExpression
        {
            Condition = condition,
            TrueValue = whenTrue,
            FalseValue = whenFalse
        };
    }

    internal static Expression HandleArrayCreationExpressionLegacy(ArrayCreationExpressionSyntax syntax, TranspilationContext ctx) {
        if (syntax.Initializer is { } initializer) {
            return BuildTableConstructor(initializer, ctx);
        }

        if (syntax.Type.RankSpecifiers.Count != 1) {
            throw new NotSupportedException("Only single-dimension arrays are supported.");
        }

        var rankSpecifier = syntax.Type.RankSpecifiers[0];
        if (rankSpecifier.Sizes.Count != 1) {
            throw new NotSupportedException("Only single-dimension arrays are supported.");
        }

        var sizeSyntax = rankSpecifier.Sizes[0];
        if (sizeSyntax is OmittedArraySizeExpressionSyntax) {
            throw new NotSupportedException("Array creation with omitted size requires an initializer.");
        }

        var sizeExpression = BuildFromSyntax(sizeSyntax, ctx);
        return FunctionCallAst.Basic("table.create", sizeExpression);
    }

    internal static Expression HandleImplicitArrayCreationExpressionLegacy(ImplicitArrayCreationExpressionSyntax syntax, TranspilationContext ctx) {
        return BuildTableConstructor(syntax.Initializer, ctx);
    }

    internal static Expression HandleElementAccessExpressionLegacy(ElementAccessExpressionSyntax syntax, TranspilationContext ctx) {
        if (syntax.ArgumentList.Arguments.Count != 1) {
            throw new NotSupportedException("Only single-dimension element access is supported.");
        }

        var indexSyntax = syntax.ArgumentList.Arguments[0].Expression;
        var target = BuildFromSyntax(syntax.Expression, ctx);
        var index = BuildFromSyntax(indexSyntax, ctx);

        var targetType = ctx.Semantics.GetTypeInfo(syntax.Expression).Type;
        var indexType = ctx.Semantics.GetTypeInfo(indexSyntax).Type;

        index = MaybeAdjustIndexForLuau(targetType, indexType, index);

        return new IndexExpression {
            Target = target,
            Index = index,
        };
    }

    private static Expression HandleSpreadElement(SpreadElementSyntax syntax, TranspilationContext ctx) {
        var expression = BuildFromSyntax(syntax.Expression, ctx);
        return FunctionCallAst.Basic("table.unpack", expression);
    }

    private static Expression MaybeAdjustIndexForLuau(ITypeSymbol? targetType, ITypeSymbol? indexType, Expression index) {
        if (targetType is null || indexType is null) {
            return index;
        }

        if (IsDictionaryType(targetType)) {
            return index;
        }

        if (!IsIntegerLike(indexType)) {
            return index;
        }

        return index switch {
            NumberExpression number => NumberExpression.From(number.Value + 1),
            _ => new BinaryOperatorExpression {
                Left = index,
                Op = BinOp.Plus,
                Right = NumberExpression.From(1),
            },
        };
    }

    private static bool IsIntegerLike(ITypeSymbol? symbol) {
        if (symbol is null) {
            return false;
        }

        if (SharedConstants.INTEGER_TYPES.Contains(symbol.Name)
         || SharedConstants.INTEGER_TYPES.Contains(symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))) {
            return true;
        }

        return symbol.SpecialType switch {
            SpecialType.System_SByte => true,
            SpecialType.System_Byte => true,
            SpecialType.System_Int16 => true,
            SpecialType.System_UInt16 => true,
            SpecialType.System_Int32 => true,
            SpecialType.System_UInt32 => true,
            SpecialType.System_Int64 => true,
            SpecialType.System_UInt64 => true,
            _ => false,
        };
    }

    private static Expression HandleAwaitExpression(AwaitExpressionSyntax syntax, TranspilationContext ctx)
    {
        var awaitedExpression = BuildFromSyntax(syntax.Expression, ctx);
        return FunctionCallAst.Basic("CS.await", awaitedExpression);
    }

    private static TableConstructor BuildTableConstructor(InitializerExpressionSyntax initializer, TranspilationContext ctx) {
        var fields = new List<TableField>();

        foreach (var expressionSyntax in initializer.Expressions) {
            Expression valueExpression;

            if (expressionSyntax is RangeExpressionSyntax rangeExpression &&
                rangeExpression.LeftOperand is null &&
                rangeExpression.RightOperand is not null) {
                var spreadExpression = BuildFromSyntax(rangeExpression.RightOperand, ctx);
                valueExpression = FunctionCallAst.Basic("table.unpack", spreadExpression);
            } else {
                valueExpression = BuildFromSyntax(expressionSyntax, ctx);
            }

            fields.Add(new NoKey { Expression = valueExpression });
        }

        return new TableConstructor { Fields = fields };
    }

internal static Expression HandleCollectionExpressionLegacy(CollectionExpressionSyntax syntax, TranspilationContext ctx) {
        var fields = new List<TableField>();
        var typeInfo = ctx.Semantics.GetTypeInfo(syntax);
        var targetType = typeInfo.ConvertedType ?? typeInfo.Type;
        var isDictionary = IsDictionaryType(targetType);

        foreach (var element in syntax.Elements) {
            switch (element) {
                case ExpressionElementSyntax expressionElement: {
                    if (isDictionary) {
                        AddDictionaryElement(fields, expressionElement.Expression, null, ctx);
                    } else {
                        var value = BuildFromSyntax(expressionElement.Expression, ctx);
                        fields.Add(new NoKey { Expression = value });
                    }

                    break;
                }

                case SpreadElementSyntax spreadElement: {
                    if (isDictionary) {
                        throw new NotSupportedException("Dictionary spreads are not supported yet.");
                    }

                    var spreadExpression = BuildFromSyntax(spreadElement.Expression, ctx);
                    fields.Add(new NoKey {
                        Expression = FunctionCallAst.Basic("table.unpack", spreadExpression),
                    });

                    break;
                }

                default:
                    throw new NotSupportedException($"Collection element {element.Kind()} is not supported.");
            }
        }

        return new TableConstructor { Fields = fields };
    }

    private static Expression BuildDictionaryInitializer(InitializerExpressionSyntax initializer, TranspilationContext ctx) {
        var fields = new List<TableField>();

        foreach (var expression in initializer.Expressions) {
            switch (expression) {
                case AssignmentExpressionSyntax assignment: {
                    var keySyntax = ExtractDictionaryKeySyntax(assignment.Left);
                    var keyExpression = BuildFromSyntax(keySyntax, ctx);
                    var valueExpression = BuildFromSyntax(assignment.Right, ctx);
                    fields.Add(new ComputedKey { Key = keyExpression, Value = valueExpression });
                    break;
                }

                case InitializerExpressionSyntax pair when pair.Expressions.Count >= 2: {
                    var keyExpression = BuildFromSyntax(pair.Expressions[0], ctx);
                    var valueExpression = BuildFromSyntax(pair.Expressions[1], ctx);
                    fields.Add(new ComputedKey { Key = keyExpression, Value = valueExpression });
                    break;
                }

                default:
                    throw new NotSupportedException($"Dictionary initializer expression {expression.Kind()} is not supported.");
            }
        }

        return new TableConstructor { Fields = fields };
    }

    private static void AddDictionaryElement(List<TableField> fields, ExpressionSyntax expression, ExpressionSyntax? valueSyntax, TranspilationContext ctx) {
        if (expression is AssignmentExpressionSyntax assignment) {
            var keySyntax = ExtractDictionaryKeySyntax(assignment.Left);
            var key = BuildFromSyntax(keySyntax, ctx);
            var value = BuildFromSyntax(assignment.Right, ctx);
            fields.Add(new ComputedKey { Key = key, Value = value });
            return;
        }

        if (expression is InitializerExpressionSyntax initializer && initializer.Expressions.Count >= 2) {
            var key = BuildFromSyntax(initializer.Expressions[0], ctx);
            var value = BuildFromSyntax(initializer.Expressions[1], ctx);
            fields.Add(new ComputedKey { Key = key, Value = value });
            return;
        }

        if (valueSyntax is not null) {
            var key = BuildFromSyntax(expression, ctx);
            var value = BuildFromSyntax(valueSyntax, ctx);
            fields.Add(new ComputedKey { Key = key, Value = value });
            return;
        }

        throw new NotSupportedException($"Dictionary collection element {expression.Kind()} is not supported.");
    }

    private static ExpressionSyntax ExtractDictionaryKeySyntax(ExpressionSyntax syntax) {
        return syntax switch {
            ImplicitElementAccessSyntax implicitAccess when implicitAccess.ArgumentList.Arguments.Count > 0
                => implicitAccess.ArgumentList.Arguments[0].Expression,
            ElementAccessExpressionSyntax elementAccess when elementAccess.ArgumentList.Arguments.Count > 0
                => elementAccess.ArgumentList.Arguments[0].Expression,
            _ => syntax,
        };
    }

    internal static bool IsDictionaryType(ITypeSymbol? symbol) {
        if (symbol is null) return false;

        if (symbol is INamedTypeSymbol namedSymbol) {
            if (IsDictionaryNamedType(namedSymbol)) {
                return true;
            }

            foreach (var interfaceType in namedSymbol.AllInterfaces) {
                if (interfaceType is INamedTypeSymbol namedInterface && IsDictionaryNamedType(namedInterface)) {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsDictionaryNamedType(INamedTypeSymbol symbol) {
        if (symbol.Name.StartsWith("Dictionary", StringComparison.Ordinal)) {
            return true;
        }

        if (symbol.Name is "IDictionary" or "IReadOnlyDictionary") {
            var ns = symbol.ContainingNamespace?.ToDisplayString();
            return ns is "System.Collections.Generic" or "System.Collections";
        }

        return false;
    }

    private static Expression HandleParenthesizedLambda(ParenthesizedLambdaExpressionSyntax syntax, TranspilationContext ctx) {
        var methodSymbol = ResolveAnonymousFunctionSymbol(syntax, ctx);
        return BuildAnonymousFunctionExpression(methodSymbol, syntax.Body, syntax.AsyncKeyword.RawKind != 0, ctx);
    }

    private static Expression HandleSimpleLambda(SimpleLambdaExpressionSyntax syntax, TranspilationContext ctx) {
        var methodSymbol = ResolveAnonymousFunctionSymbol(syntax, ctx);
        return BuildAnonymousFunctionExpression(methodSymbol, syntax.Body, syntax.AsyncKeyword.RawKind != 0, ctx);
    }

    private static Expression HandleAnonymousMethod(AnonymousMethodExpressionSyntax syntax, TranspilationContext ctx) {
        var methodSymbol = ResolveAnonymousFunctionSymbol(syntax, ctx);
        var body = syntax.Block ?? throw new NotSupportedException("Anonymous methods without a body are not supported.");
        return BuildAnonymousFunctionExpression(methodSymbol, body, syntax.AsyncKeyword.RawKind != 0, ctx);
    }

    internal static Expression HandleConditionalAccessExpressionLegacy(ConditionalAccessExpressionSyntax syntax, TranspilationContext ctx)
    {
        var typeInfo = ctx.Semantics.GetTypeInfo(syntax);
        var returnSymbol = typeInfo.ConvertedType ?? typeInfo.Type;
        var returnType = returnSymbol is ITypeSymbol symbol
            ? SyntaxUtilities.TypeInfoFromSymbol(symbol)
            : BasicTypeInfo.FromString("any");

        var block = Block.Empty();
        var resultExpression = LowerConditionalAccessExpression(syntax, ctx, block);
        block.AddStatement(Return.FromExpressions([resultExpression]));

        var functionBody = new FunctionBody
        {
            Parameters = [],
            TypeSpecifiers = [],
            ReturnType = returnType,
            Body = block,
        };

        var anonymousFunction = new AnonymousFunction { Body = functionBody };

        return new FunctionCallAst
        {
            Prefix = new ExpressionPrefix { Expression = anonymousFunction },
            Suffixes =
            [
                new AnonymousCall
                {
                    Arguments = new FunctionArgs
                    {
                        Arguments = [],
                    },
                },
            ],
        };
    }

    private static Expression LowerConditionalAccessExpression(
        ConditionalAccessExpressionSyntax syntax,
        TranspilationContext ctx,
        Block block)
    {
        var baseValue = BuildFromSyntax(syntax.Expression, ctx);
        var tempName = ctx.AllocateTempName("optional", "target");
        var tempSymbol = SymbolExpression.FromString(tempName);

        block.AddStatement(new LocalAssignment
        {
            Names = [tempSymbol],
            Expressions = [baseValue],
            Types = [],
        });

        block.AddStatement(CreateReturnNilGuard(tempSymbol));

        return BuildConditionalAccessContinuation(syntax.WhenNotNull, tempSymbol, ctx);
    }

    private static If CreateReturnNilGuard(SymbolExpression tempSymbol)
    {

        var condition = new BinaryOperatorExpression
        {
            Left = tempSymbol,
            Op = BinOp.TwoEqual,
            Right = SymbolExpression.FromString("nil"),
        };

        var thenBlock = Block.Empty();
        thenBlock.AddStatement(Return.FromExpressions([SymbolExpression.FromString("nil")]));

        return new If
        {
            Condition = condition,
            ThenBody = thenBlock,
        };
    }

    private static Expression BuildConditionalAccessContinuation(
        ExpressionSyntax whenNotNull,
        SymbolExpression tempSymbol,
        TranspilationContext ctx)
    {
        return whenNotNull switch
        {
            InvocationExpressionSyntax invocation => BuildInvocationForConditionalAccess(invocation, tempSymbol, ctx),
            MemberBindingExpressionSyntax memberBinding => SymbolExpression.FromString($"{tempSymbol.Value}.{GetSimpleName(memberBinding.Name)}"),
            ElementBindingExpressionSyntax elementBinding => BuildElementBindingExpression(elementBinding, tempSymbol, ctx),
            _ => BuildFromSyntax(whenNotNull, ctx),
        };
    }

    private static FunctionCallAst BuildInvocationForConditionalAccess(
        InvocationExpressionSyntax invocation,
        SymbolExpression tempSymbol,
        TranspilationContext ctx)
    {
        var methodSymbol = ctx.Semantics.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var arguments = invocation.ArgumentList.Arguments
            .Select(argument => BuildFromSyntax(argument.Expression, ctx))
            .ToArray();

        string callName;
        if (methodSymbol is IMethodSymbol { MethodKind: MethodKind.ReducedExtension, ReducedFrom: { } originalExtension })
        {
            callName = $"{tempSymbol.Value}:{originalExtension.Name}";
        }
        else if (methodSymbol is { IsStatic: true, ContainingType: { } containingType })
        {
            callName = $"{containingType.Name}.{methodSymbol.Name}";
        }
        else
        {
            var methodName = invocation.Expression is MemberBindingExpressionSyntax memberBinding
                ? GetSimpleName(memberBinding.Name)
                : methodSymbol?.Name ?? invocation.Expression.ToString();

            var separator = methodSymbol is { IsStatic: true } ? "." : ":";
            callName = $"{tempSymbol.Value}{separator}{methodName}";
        }

        return FunctionCallAst.Basic(callName, arguments);
    }

    private static Expression BuildElementBindingExpression(
        ElementBindingExpressionSyntax elementBinding,
        SymbolExpression tempSymbol,
        TranspilationContext ctx)
    {
        if (elementBinding.ArgumentList.Arguments.Count != 1)
        {
            throw new NotSupportedException("Only single-dimension element binding is supported.");
        }

        var argument = elementBinding.ArgumentList.Arguments[0];
        var indexExpression = BuildFromSyntax(argument.Expression, ctx);

        ITypeSymbol? targetType = null;
        if (elementBinding.Parent is ConditionalAccessExpressionSyntax parentConditional)
        {
            targetType = ctx.Semantics.GetTypeInfo(parentConditional.Expression).Type;
        }

        var indexType = ctx.Semantics.GetTypeInfo(argument.Expression).Type;
        var adjustedIndex = MaybeAdjustIndexForLuau(targetType, indexType, indexExpression);

        return new IndexExpression
        {
            Target = tempSymbol,
            Index = adjustedIndex,
        };
    }

    private static Expression BuildAnonymousFunctionExpression(
        IMethodSymbol methodSymbol,
        CSharpSyntaxNode bodyNode,
        bool isAsync,
        TranspilationContext ctx
    ) {
        var blockSyntax = bodyNode as BlockSyntax;
        ArrowExpressionClauseSyntax? expressionBody = bodyNode is ExpressionSyntax expression
            ? SyntaxFactory.ArrowExpressionClause(expression)
            : null;

        var functionBody = FunctionBuilder.CreateAnonymousFunctionBody(methodSymbol, blockSyntax, expressionBody, ctx);
        var anonymousFunction = new AnonymousFunction { Body = functionBody };

        var shouldWrapAsync = isAsync || methodSymbol.IsAsync;
        if (shouldWrapAsync) {
            ctx.MarkAsync(methodSymbol);
            return FunctionCallAst.Basic("CS.async", anonymousFunction);
        }

        return anonymousFunction;
    }

    internal static bool IsMethodGroupContext(ExpressionSyntax syntax)
    {
        return syntax.Parent switch
        {
            InvocationExpressionSyntax => false,
            MemberBindingExpressionSyntax => false,
            ConditionalAccessExpressionSyntax => false,
            _ => true,
        };
    }

    internal static Expression BuildMethodGroupExpression(ExpressionSyntax syntax, IMethodSymbol methodSymbol, TranspilationContext ctx)
    {
        if (methodSymbol.IsStatic)
        {
            var staticReference = $"{BuildTypeAccess(methodSymbol.ContainingType, ctx)}.{methodSymbol.Name}";
            return SymbolExpression.FromString(staticReference);
        }

        if (syntax is IdentifierNameSyntax)
        {
            return SymbolExpression.FromString($"self.{methodSymbol.Name}");
        }

        if (syntax is MemberAccessExpressionSyntax memberAccess)
        {
            var target = BuildAccessString(memberAccess.Expression, ctx);
            var methodName = GetSimpleName(memberAccess.Name);
            return SymbolExpression.FromString($"{target}.{methodName}");
        }

        var access = BuildAccessString(syntax, ctx);
        return SymbolExpression.FromString(access);
    }

    private static IMethodSymbol ResolveAnonymousFunctionSymbol(CSharpSyntaxNode syntax, TranspilationContext ctx) {
        var symbolInfo = ctx.Semantics.GetSymbolInfo(syntax);
        if (symbolInfo.Symbol is IMethodSymbol methodSymbol) {
            return methodSymbol;
        }

        foreach (var candidate in symbolInfo.CandidateSymbols) {
            if (candidate is IMethodSymbol candidateMethod) {
                return candidateMethod;
            }
        }

        var convertedType = ctx.Semantics.GetTypeInfo(syntax).ConvertedType as INamedTypeSymbol;
        if (convertedType?.DelegateInvokeMethod is IMethodSymbol invokeMethod) {
            return invokeMethod;
        }

        throw new NotSupportedException($"Unable to resolve lambda symbol for syntax kind {syntax.Kind()}.");
    }

    private static Expression HandleBinaryExpressionSyntax(BinaryExpressionSyntax syntax, TranspilationContext ctx) {
        return syntax.Kind() switch {
            SyntaxKind.AddExpression => HandleBinaryExpression(syntax, ctx),
            SyntaxKind.SubtractExpression => HandleBinaryExpression(syntax, ctx),
            SyntaxKind.MultiplyExpression => HandleBinaryExpression(syntax, ctx),
            SyntaxKind.DivideExpression => HandleBinaryExpression(syntax, ctx),
            SyntaxKind.ModuloExpression => HandleBinaryExpression(syntax, ctx),
            SyntaxKind.GreaterThanExpression => HandleBinaryExpression(syntax, ctx),
            SyntaxKind.GreaterThanOrEqualExpression => HandleBinaryExpression(syntax, ctx),
            SyntaxKind.LessThanExpression => HandleBinaryExpression(syntax, ctx),
            SyntaxKind.LessThanOrEqualExpression => HandleBinaryExpression(syntax, ctx),
            SyntaxKind.EqualsExpression => HandleBinaryExpression(syntax, ctx),
            SyntaxKind.NotEqualsExpression => HandleBinaryExpression(syntax, ctx),
            SyntaxKind.LogicalAndExpression => HandleBinaryExpression(syntax, ctx),
            SyntaxKind.LogicalOrExpression => HandleBinaryExpression(syntax, ctx),

            _ => throw new NotSupportedException($"BinaryExpressionSyntax {syntax.Kind()} is not supported."),
        };
    }

    private static BinaryOperatorExpression HandleBinaryExpression(BinaryExpressionSyntax syntax, TranspilationContext ctx) {
        var left = syntax.Left;
        var right = syntax.Right;

        var tLeft = BuildFromSyntax(left, ctx);
        var tRight = BuildFromSyntax(right, ctx);
        var tOp = SyntaxUtilities.SyntaxTokenToBinOp(syntax.OperatorToken);

        return new BinaryOperatorExpression {
            Left = tLeft,
            Right = tRight,
            Op = tOp,
        };
    }

    internal static Expression HandleLiteralExpressionLegacy(LiteralExpressionSyntax syntax, TranspilationContext ctx)
    {
        return syntax.Kind() switch
        {
            SyntaxKind.NumericLiteralExpression => HandleNumericLiteralExpression(syntax, ctx),
            SyntaxKind.StringLiteralExpression => HandleStringLiteralExpression(syntax, ctx),
            SyntaxKind.TrueLiteralExpression => HandleBooleanLiteralExpression(syntax, ctx),
            SyntaxKind.FalseLiteralExpression => HandleBooleanLiteralExpression(syntax, ctx),
            SyntaxKind.NullLiteralExpression => SymbolExpression.FromString("nil"),
            _ => throw new NotSupportedException($"LiteralExpressionSyntax {syntax.Kind()} is not supported."),
        };
    }

    private static Expression HandlePrefixUnaryExpression(PrefixUnaryExpressionSyntax syntax, TranspilationContext ctx) {
        var operand = BuildFromSyntax(syntax.Operand, ctx);

        return syntax.Kind() switch {
            SyntaxKind.UnaryPlusExpression => operand,
            SyntaxKind.UnaryMinusExpression => new UnaryOperatorExpression {
                Op = UnOp.Minus,
                Operand = operand,
            },
            SyntaxKind.LogicalNotExpression => new UnaryOperatorExpression {
                Op = UnOp.Not,
                Operand = operand,
            },
            SyntaxKind.BitwiseNotExpression => new UnaryOperatorExpression {
                Op = UnOp.BitwiseNot,
                Operand = operand,
            },

            _ => throw new NotSupportedException($"PrefixUnaryExpressionSyntax {syntax.Kind()} is not supported."),
        };
    }

    private static NumberExpression HandleNumericLiteralExpression(LiteralExpressionSyntax syntax, TranspilationContext ctx) {
        var value = syntax.Token.Value!;

        return NumberExpression.From(Convert.ToDouble(value));
    }

    private static StringExpression HandleStringLiteralExpression(LiteralExpressionSyntax syntax, TranspilationContext ctx) {
        return StringExpression.FromString(syntax.Token.ValueText);
    }

    private static Expression HandleInterpolatedStringExpression(InterpolatedStringExpressionSyntax syntax, TranspilationContext ctx)
    {
        var parts = new List<InterpolatedStringPart>();

        foreach (var content in syntax.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    if (!string.IsNullOrEmpty(text.TextToken.Text))
                    {
                        parts.Add(new InterpolatedStringTextPart
                        {
                            Text = text.TextToken.Text,
                        });
                    }
                    break;

                case InterpolationSyntax interpolation:
                    parts.Add(new InterpolatedStringExpressionPart
                    {
                        Expression = BuildInterpolationPart(interpolation, ctx),
                    });
                    break;

                default:
                    throw new NotSupportedException($"Interpolated string content {content.Kind()} is not supported.");
            }
        }

        return parts.Count == 0
            ? StringExpression.FromString(string.Empty)
            : new InterpolatedStringExpression { Parts = parts };
    }

    private static Expression BuildInterpolationPart(InterpolationSyntax interpolation, TranspilationContext ctx)
    {
        if (interpolation.AlignmentClause is not null)
        {
            throw new NotSupportedException("Interpolated string alignment clauses are not supported.");
        }

        if (interpolation.FormatClause is not null)
        {
            throw new NotSupportedException("Interpolated string format clauses are not supported.");
        }

        return BuildFromSyntax(interpolation.Expression, ctx);
    }

    private static Expression HandleTupleExpression(TupleExpressionSyntax tupleExpression, TranspilationContext ctx)
    {
        var fields = new List<TableField>(tupleExpression.Arguments.Count);

        for (var index = 0; index < tupleExpression.Arguments.Count; index++)
        {
            var argument = tupleExpression.Arguments[index];
            var valueExpression = BuildFromSyntax(argument.Expression, ctx);

            fields.Add(new NameKey
            {
                Key = $"Item{index + 1}",
                Value = valueExpression,
            });
        }

        return new TableConstructor { Fields = fields };
    }

    private static BooleanExpression HandleBooleanLiteralExpression(LiteralExpressionSyntax syntax, TranspilationContext ctx) {
        var value = syntax.Kind() == SyntaxKind.TrueLiteralExpression;
        return new BooleanExpression { Value = value };
    }

    private static Expression CreateConstantExpression(object? value)
    {
        return value switch
        {
            null => SymbolExpression.FromString("nil"),
            string s => StringExpression.FromString(s),
            char c => StringExpression.FromString(c.ToString()),
            bool b => SymbolExpression.FromString(b ? "true" : "false"),
            sbyte or byte or short or ushort or uint or int or uint or long or ulong or float or double or decimal
                => NumberExpression.From(Convert.ToDouble(value)),
            _ => StringExpression.FromString(value.ToString() ?? string.Empty),
        };
    }

    private static Expression HandleIdentifierNameSyntax(IdentifierNameSyntax syntax, TranspilationContext ctx) {
        if (syntax.SyntaxTree != ctx.Root.SyntaxTree)
        {
            return SymbolExpression.FromString(syntax.Identifier.ValueText);
        }

        var symbol = ctx.Semantics.GetSymbolInfo(syntax).Symbol;
        if (symbol is null) throw new Exception($"Semantics failed to get symbol info for {syntax.Identifier.ValueText}.");

        return symbol switch {
            IParameterSymbol parameterSymbol => SymbolExpression.FromString(parameterSymbol.Name),
            ILocalSymbol localSymbol => SymbolExpression.FromString(localSymbol.Name),
            IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum, HasConstantValue: true } enumField
                => CreateConstantExpression(enumField.ConstantValue),
            IFieldSymbol fieldSymbol => fieldSymbol.IsStatic
                ? SymbolExpression.FromString($"{BuildTypeAccess(fieldSymbol.ContainingType, ctx)}.{fieldSymbol.Name}")
                : SymbolExpression.FromString($"self.{fieldSymbol.Name}"),
            IPropertySymbol propertySymbol => propertySymbol.IsStatic
                ? SymbolExpression.FromString($"{BuildTypeAccess(propertySymbol.ContainingType, ctx)}.{propertySymbol.Name}")
                : SymbolExpression.FromString($"self.{propertySymbol.Name}"),
            IEventSymbol eventSymbol => eventSymbol.IsStatic
                ? SymbolExpression.FromString($"{BuildTypeAccess(eventSymbol.ContainingType, ctx)}.{eventSymbol.Name}")
                : SymbolExpression.FromString($"self.{eventSymbol.Name}"),
            IMethodSymbol methodSymbol when IsMethodGroupContext(syntax) => BuildMethodGroupExpression(syntax, methodSymbol, ctx),
            IDiscardSymbol => SymbolExpression.FromString(RobloxCS.Luau.AstUtility.DiscardName.Text),

            _ => throw new NotSupportedException($"IdentifierNameSyntax {symbol.Kind} is not supported."),
        };
    }

    private static Expression HandleMemberAccessExpression(MemberAccessExpressionSyntax syntax, TranspilationContext ctx) {
        if (syntax.SyntaxTree != ctx.Root.SyntaxTree)
        {
            var rawAccess = syntax.ToString();
            return SymbolExpression.FromString(rawAccess);
        }

        var symbol = ctx.Semantics.GetSymbolInfo(syntax).Symbol;

        if (symbol is IMethodSymbol methodSymbol && IsMethodGroupContext(syntax)) {
            return BuildMethodGroupExpression(syntax, methodSymbol, ctx);
        }

        if (symbol is INamedTypeSymbol typeSymbol)
        {
            return SymbolExpression.FromString(BuildTypeAccess(typeSymbol, ctx));
        }

        if (symbol is IEventSymbol eventSymbol)
        {
            if (eventSymbol.IsStatic)
            {
                return SymbolExpression.FromString($"{ctx.GetTypeName(eventSymbol.ContainingType)}.{eventSymbol.Name}");
            }

            var access = BuildAccessString(syntax, ctx);
            return SymbolExpression.FromString(access);
        }

        if (symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum, HasConstantValue: true } enumField)
        {
            return CreateConstantExpression(enumField.ConstantValue);
        }

        if (symbol is IPropertySymbol propertySymbol && TryBuildSliceLengthExpression(syntax, propertySymbol, ctx, out var lengthExpression))
        {
            return lengthExpression;
        }

        if (TryBuildStaticPropertyMacro(symbol, ctx, out var macroExpression))
        {
            return macroExpression;
        }

        var accessString = BuildAccessString(syntax.Expression, ctx);
        var memberName = GetSimpleName(syntax.Name);
        return SymbolExpression.FromString($"{accessString}.{memberName}");
    }

    internal static FunctionCallAst BuildFunctionCall(InvocationExpressionSyntax syntax, TranspilationContext ctx) {
        var callName = BuildCallName(syntax.Expression, ctx);
        var methodSymbol = ctx.Semantics.GetSymbolInfo(syntax.Expression).Symbol as IMethodSymbol;

        var arguments = new List<Expression>();
        for (var index = 0; index < syntax.ArgumentList.Arguments.Count; index++)
        {
            var argumentSyntax = syntax.ArgumentList.Arguments[index];

            if (argumentSyntax.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || argumentSyntax.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))
            {
                if (methodSymbol is null || index >= methodSymbol.Parameters.Length)
                {
                    throw new NotSupportedException("Unable to resolve method parameter for ref/out argument.");
                }

                arguments.Add(BuildRefArgumentExpression(argumentSyntax, methodSymbol.Parameters[index], ctx));
                continue;
            }

            arguments.Add(BuildFromSyntax(argumentSyntax.Expression, ctx));
        }

        return FunctionCallAst.Basic(callName, arguments.ToArray());
    }

    internal static bool TryBuildInvocationMacroExpression(InvocationExpressionSyntax syntax, TranspilationContext ctx, out Expression expression)
    {
        expression = null!;

        if (TryGetInvocationIdentifier(syntax.Expression, out var identifier)
            && TryBuildIdentifierMacro(syntax, ctx, identifier, out expression))
        {
            return true;
        }

        if (TryBuildNameof(syntax, ctx, out expression))
        {
            return true;
        }

        if (ctx.Semantics.GetSymbolInfo(syntax.Expression).Symbol is not IMethodSymbol methodSymbol)
        {
            return false;
        }

        if (TryBuildSystemMathInvocation(syntax, ctx, methodSymbol, out expression))
        {
            return true;
        }

        if (TryBuildBit32Invocation(syntax, ctx, methodSymbol, out expression))
        {
            return true;
        }

        if (TryBuildUnityAliasesInvocation(syntax, ctx, methodSymbol, out expression))
        {
            return true;
        }

        if (TryBuildIDivInvocation(syntax, ctx, methodSymbol, out expression))
        {
            return true;
        }

        if (TryBuildTSHelperInvocation(syntax, ctx, methodSymbol, out expression))
        {
            return true;
        }

        if (TryBuildStringMacro(syntax, ctx, methodSymbol, out expression))
        {
            return true;
        }

        if (TryBuildGlobalsInvocation(syntax, ctx, methodSymbol, out expression))
        {
            return true;
        }

        return false;
    }

    private static bool TryBuildGlobalsInvocation(
        InvocationExpressionSyntax syntax,
        TranspilationContext ctx,
        IMethodSymbol methodSymbol,
        out Expression expression)
    {
        expression = null!;

        if (methodSymbol.ContainingType.Name != "Globals" ||
            (methodSymbol.ContainingType.ContainingNamespace?.Name != "Roblox" &&
             methodSymbol.ContainingType.ContainingNamespace?.ToDisplayString() != "Roblox"))
        {
            return false;
        }

        string? luaFunction = methodSymbol.Name switch
        {
            "print" => "print",
            "pcall" => "pcall",
            "pairs" => "pairs",
            "ipairs" => "ipairs",
            "typeIs" => "CS.typeIs",
            "classIs" => "CS.classIs",
            _ => null
        };

        if (luaFunction == null)
            return false;

        var args = syntax.ArgumentList.Arguments.Select(a => BuildFromSyntax(a.Expression, ctx));
        expression = FunctionCallAst.Basic(luaFunction, args.ToArray());
        return true;
    }

    private static bool TryBuildUnityAliasesInvocation(
        InvocationExpressionSyntax syntax,
        TranspilationContext ctx,
        IMethodSymbol methodSymbol,
        out Expression expression)
    {
        expression = null!;

        if (methodSymbol.ContainingType.Name != "UnityAliases" ||
            (methodSymbol.ContainingType.ContainingNamespace?.Name != "Roblox" &&
             methodSymbol.ContainingType.ContainingNamespace?.ToDisplayString() != "Roblox"))
        {
            return false;
        }

        string? luaFunction = methodSymbol.Name switch
        {
            "Log" => "print",
            "LogWarning" => "warn",
            "LogError" => "error",
            _ => null
        };

        if (luaFunction == null)
            return false;

        var args = syntax.ArgumentList.Arguments.Select(a => BuildFromSyntax(a.Expression, ctx));
        expression = FunctionCallAst.Basic(luaFunction, args.ToArray());
        return true;
    }

    private static bool TryBuildStringMacro(
        InvocationExpressionSyntax syntax,
        TranspilationContext ctx,
        IMethodSymbol methodSymbol,
        out Expression expression)
    {
        expression = null!;

        if (methodSymbol.ContainingType.SpecialType != SpecialType.System_String)
        {
            return false;
        }

        if (methodSymbol.Name == "IsNullOrWhiteSpace")
        {
            var argument = BuildFromSyntax(syntax.ArgumentList.Arguments[0].Expression, ctx);
            
            var isNil = new BinaryOperatorExpression {
                Left = argument,
                Op = BinOp.TwoEqual,
                Right = SymbolExpression.FromString("nil")
            };
            
            var matchPrefix = argument is SymbolExpression sym
                ? (Prefix)new NamePrefix { Name = sym.Value }
                : new ExpressionPrefix { Expression = (Expression)argument.DeepClone() };

            var matchCall = new FunctionCall
            {
                Prefix = matchPrefix,
                Suffixes = [
                     new MethodCall {
                         Name = "match",
                         Args = new FunctionArgs {
                             Arguments = [StringExpression.FromString("^%s*$")]
                         }
                     }
                ]
            };

            var isWhitespace = new BinaryOperatorExpression {
                Left = matchCall,
                Op = BinOp.TildeEqual,
                Right = SymbolExpression.FromString("nil")
            };

            expression = new BinaryOperatorExpression {
                Left = isNil,
                Op = BinOp.Or,
                Right = isWhitespace
            };
            return true;
        }

        if (methodSymbol.Name == "IsNullOrEmpty")
        {
             var argument = BuildFromSyntax(syntax.ArgumentList.Arguments[0].Expression, ctx);
             
             var isNil = new BinaryOperatorExpression {
                 Left = argument,
                 Op = BinOp.TwoEqual,
                 Right = SymbolExpression.FromString("nil")
             };
             
             var isEmpty = new BinaryOperatorExpression {
                 Left = (Expression)argument.DeepClone(),
                 Op = BinOp.TwoEqual,
                 Right = StringExpression.FromString("")
             };
             
             expression = new BinaryOperatorExpression {
                 Left = isNil,
                 Op = BinOp.Or,
                 Right = isEmpty
             };
             return true;
        }
        
        return false;
    }

    private static bool TryGetInvocationIdentifier(ExpressionSyntax expression, out string identifier)
    {
        switch (expression)
        {
            case IdentifierNameSyntax name:
                identifier = name.Identifier.ValueText;
                return true;
            case MemberAccessExpressionSyntax memberAccess when memberAccess.Expression is IdentifierNameSyntax { Identifier.ValueText: "CS" }:
                identifier = memberAccess.Name.Identifier.ValueText;
                return true;
            default:
                identifier = string.Empty;
                return false;
        }
    }

    private static bool TryBuildIdentifierMacro(
        InvocationExpressionSyntax syntax,
        TranspilationContext ctx,
        string identifier,
        out Expression expression)
    {
        expression = null!;

        if (identifier is not ("typeIs" or "classIs"))
        {
            return false;
        }

        if (syntax.ArgumentList.Arguments.Count != 2)
        {
            return false;
        }

        var valueExpression = BuildFromSyntax(syntax.ArgumentList.Arguments[0].Expression, ctx);
        var typeExpression = BuildFromSyntax(syntax.ArgumentList.Arguments[1].Expression, ctx);

        expression = identifier switch
        {
            "typeIs" => FunctionCallAst.Basic("CS.typeIs", valueExpression, typeExpression),
            "classIs" => FunctionCallAst.Basic("CS.classIs", valueExpression, typeExpression),
            _ => expression,
        };

        return true;
    }

    private sealed record MathMacroDefinition(string FunctionName, int MinArgs, int? MaxArgs = null)
    {
        public bool SupportsCount(int count) => count >= MinArgs && count <= (MaxArgs ?? MinArgs);
    }

    private static readonly HashSet<string> SystemMathTypeNames = new(StringComparer.Ordinal)
    {
        "System.Math",
        "System.MathF",
    };

    private static readonly Dictionary<string, MathMacroDefinition> MathMacroDefinitions = new(StringComparer.Ordinal)
    {
        [nameof(Math.Abs)] = new("math.abs", 1),
        [nameof(Math.Acos)] = new("math.acos", 1),
        [nameof(Math.Asin)] = new("math.asin", 1),
        [nameof(Math.Atan)] = new("math.atan", 1),
        [nameof(Math.Atan2)] = new("math.atan2", 2),
        [nameof(Math.Ceiling)] = new("math.ceil", 1),
        [nameof(Math.Clamp)] = new("math.clamp", 3),
        [nameof(Math.Cos)] = new("math.cos", 1),
        [nameof(Math.Exp)] = new("math.exp", 1),
        [nameof(Math.Floor)] = new("math.floor", 1),
        [nameof(Math.Log)] = new("math.log", 1),
        [nameof(Math.Log10)] = new("math.log10", 1),
        [nameof(Math.Max)] = new("math.max", 2),
        [nameof(Math.Min)] = new("math.min", 2),
        [nameof(Math.Pow)] = new("math.pow", 2),
        [nameof(Math.Round)] = new("math.round", 1),
        [nameof(Math.Sign)] = new("math.sign", 1),
        [nameof(Math.Sin)] = new("math.sin", 1),
        [nameof(Math.Sqrt)] = new("math.sqrt", 1),
        [nameof(Math.Tan)] = new("math.tan", 1),
    };

    private static bool TryBuildSystemMathInvocation(
        InvocationExpressionSyntax syntax,
        TranspilationContext ctx,
        IMethodSymbol methodSymbol,
        out Expression expression)
    {
        expression = null!;

        if (methodSymbol.ContainingType is null)
        {
            return false;
        }

        var containingName = methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (!SystemMathTypeNames.Contains(containingName))
        {
            return false;
        }

        if (!ctx.Options.MacroOptions.EnableMathMacros)
        {
            return false;
        }

        if (!MathMacroDefinitions.TryGetValue(methodSymbol.Name, out var definition))
        {
            return false;
        }

        var args = syntax.ArgumentList.Arguments
            .Select(argument => BuildFromSyntax(argument.Expression, ctx))
            .ToList();

        if (!definition.SupportsCount(args.Count))
        {
            return false;
        }

        expression = FunctionCallAst.Basic(definition.FunctionName, args.ToArray());
        return true;
    }

    private sealed record Bit32MacroDefinition(string FunctionName, int ExpectedArgumentCount)
    {
        public bool SupportsCount(int count) => count == ExpectedArgumentCount;
    }

    private static readonly Dictionary<string, Bit32MacroDefinition> Bit32MacroDefinitions = new(StringComparer.Ordinal)
    {
        ["Band"] = new("bit32.band", 2),
        ["Bor"] = new("bit32.bor", 2),
        ["Bxor"] = new("bit32.bxor", 2),
        ["BNot"] = new("bit32.bnot", 1),
        ["LShift"] = new("bit32.lshift", 2),
        ["RShift"] = new("bit32.rshift", 2),
        ["ArShift"] = new("bit32.arshift", 2),
    };

    private static bool TryBuildBit32Invocation(
        InvocationExpressionSyntax syntax,
        TranspilationContext ctx,
        IMethodSymbol methodSymbol,
        out Expression expression)
    {
        expression = null!;

        if (methodSymbol.ContainingType is null)
        {
            return false;
        }

        var containingName = methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (!string.Equals(containingName, "Roblox.Bit32", StringComparison.Ordinal))
        {
            return false;
        }

        if (!ctx.Options.MacroOptions.EnableBit32Macros)
        {
            return false;
        }

        if (!Bit32MacroDefinitions.TryGetValue(methodSymbol.Name, out var definition))
        {
            return false;
        }

        var args = syntax.ArgumentList.Arguments
            .Select(argument => BuildFromSyntax(argument.Expression, ctx))
            .ToList();

        if (!definition.SupportsCount(args.Count))
        {
            return false;
        }

        expression = FunctionCallAst.Basic(definition.FunctionName, args.ToArray());
        return true;
    }

    private static bool TryBuildIDivInvocation(
        InvocationExpressionSyntax syntax,
        TranspilationContext ctx,
        IMethodSymbol methodSymbol,
        out Expression expression)
    {
        expression = null!;

        if (!IsIDivMethod(methodSymbol))
        {
            return false;
        }

        Expression leftOperand;
        IReadOnlyList<ArgumentSyntax> arguments;

        if (methodSymbol.MethodKind == MethodKind.ReducedExtension && syntax.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            leftOperand = BuildFromSyntax(memberAccess.Expression, ctx);
            arguments = syntax.ArgumentList.Arguments;
        }
        else
        {
            arguments = syntax.ArgumentList.Arguments;
            if (arguments.Count == 0)
            {
                return false;
            }

            leftOperand = BuildFromSyntax(arguments[0].Expression, ctx);
            arguments = arguments.Skip(1).ToList();
        }

        if (arguments.Count != 1)
        {
            return false;
        }

        var rightOperand = BuildFromSyntax(arguments[0].Expression, ctx);
        expression = new BinaryOperatorExpression
        {
            Left = leftOperand,
            Op = BinOp.DoubleSlash,
            Right = rightOperand,
        };

        return true;
    }

    private static bool IsIDivMethod(IMethodSymbol methodSymbol)
    {
        if (!string.Equals(methodSymbol.Name, "idiv", StringComparison.Ordinal))
        {
            return false;
        }

        var containingType = methodSymbol.ReducedFrom?.ContainingType ?? methodSymbol.ContainingType;
        if (containingType is null)
        {
            return false;
        }

        var containingName = containingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return string.Equals(containingName, "Roblox.NumberExtensions", StringComparison.Ordinal);
    }

    private static bool TryBuildNameof(InvocationExpressionSyntax syntax, TranspilationContext ctx, out Expression expression)
    {
        expression = null!;

        if (syntax.Expression is not IdentifierNameSyntax identifier || identifier.Identifier.ValueText != "nameof")
        {
            return false;
        }

        if (syntax.ArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        var argument = syntax.ArgumentList.Arguments[0].Expression;
        var symbol = ctx.Semantics.GetSymbolInfo(argument).Symbol;
        
        // nameof(x) uses the simple name of the symbol
        if (symbol != null)
        {
            expression = StringExpression.FromString(symbol.Name);
            return true;
        }
        
        // Fallback for when semantics fail (e.g. valid C# but complex case), usually it's just the identifier text
        if (argument is IdentifierNameSyntax id)
        {
             expression = StringExpression.FromString(id.Identifier.ValueText);
             return true;
        }
        
        if (argument is MemberAccessExpressionSyntax memberAccess)
        {
             expression = StringExpression.FromString(memberAccess.Name.Identifier.ValueText);
             return true;
        }

        return false;
    }

    private static bool TryBuildTSHelperInvocation(
        InvocationExpressionSyntax syntax,
        TranspilationContext ctx,
        IMethodSymbol methodSymbol,
        out Expression expression)
    {
        expression = null!;

        var containingType = methodSymbol.ContainingType;
        if (containingType is null)
        {
            return false;
        }

        var containingName = containingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (!string.Equals(containingName, "Roblox.TS", StringComparison.Ordinal))
        {
            return false;
        }

        if (!ctx.Options.MacroOptions.EnableIteratorHelpers)
        {
            return false;
        }

        var args = syntax.ArgumentList.Arguments
            .Select(argument => BuildFromSyntax(argument.Expression, ctx))
            .ToList();

        switch (methodSymbol.Name)
        {
            case "iter" when args.Count == 1:
                expression = FunctionCallAst.Basic("CS.iter", args[0]);
                return true;
            case "array_flatten" when args.Count == 1:
                expression = FunctionCallAst.Basic("CS.array_flatten", args[0]);
                return true;
            default:
                return false;
        }
    }

    private static bool TryBuildStaticPropertyMacro(ISymbol? symbol, TranspilationContext ctx, out Expression expression)
    {
        expression = null!;

        if (symbol is not (IPropertySymbol or IFieldSymbol) || symbol.ContainingType is null)
        {
            return false;
        }

        var containingName = symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (!SystemMathTypeNames.Contains(containingName))
        {
            return false;
        }

        if (!ctx.Options.MacroOptions.EnableMathMacros)
        {
            return false;
        }

        switch (symbol.Name)
        {
            case "PI":
                expression = SymbolExpression.FromString("math.pi");
                return true;
            case "Tau":
                expression = NumberExpression.From(Math.Tau);
                return true;
            case "E":
                expression = NumberExpression.From(Math.E);
                return true;
            default:
                return false;
        }
    }

    private static Expression BuildRefArgumentExpression(ArgumentSyntax argument, IParameterSymbol parameterSymbol, TranspilationContext ctx)
    {
        var targetName = BuildRefTargetName(argument.Expression, ctx);
        var targetExpression = SymbolExpression.FromString(targetName);

        var body = Block.Empty();

        body.AddStatement(new LocalAssignment
        {
            Names = [SymbolExpression.FromString("_val")],
            Expressions = [SymbolExpression.FromString("...")],
            Types = [],
        });

        var condition = new BinaryOperatorExpression
        {
            Left = FunctionCallAst.Basic("select", StringExpression.FromString("#"), SymbolExpression.FromString("...")),
            Op = BinOp.TildeEqual,
            Right = NumberExpression.From(0),
        };

        var thenBlock = Block.Empty();
        thenBlock.AddStatement(new Assignment
        {
            Vars = [VarName.FromString(targetName)],
            Expressions = [SymbolExpression.FromString("_val")],
        });

        body.AddStatement(new If
        {
            Condition = condition,
            ThenBody = thenBlock,
        });

        body.AddStatement(Return.FromExpressions([(Expression)targetExpression.DeepClone()]));

        var functionBody = new FunctionBody
        {
            Parameters = [new EllipsisParameter()],
            TypeSpecifiers = [BasicTypeInfo.FromString("any")],
            ReturnType = SyntaxUtilities.BasicFromSymbol(parameterSymbol.Type),
            Body = body,
        };

        return new AnonymousFunction { Body = functionBody };
    }

    private static string BuildRefTargetName(ExpressionSyntax expression, TranspilationContext ctx)
    {
        if (expression is DeclarationExpressionSyntax declaration && declaration.Designation is SingleVariableDesignationSyntax single)
        {
            var localAssignment = new LocalAssignment
            {
                Names = [SymbolExpression.FromString(single.Identifier.ValueText)],
                Expressions = [],
                Types = [],
            };

            ctx.AddPrerequisite(localAssignment);
            return single.Identifier.ValueText;
        }

        if (expression is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.ValueText;
        }

        throw new NotSupportedException("ref/out arguments currently require simple identifiers or declarations.");
    }

    private static string BuildCallName(ExpressionSyntax expression, TranspilationContext ctx) {
        var symbol = ctx.Semantics.GetSymbolInfo(expression).Symbol as IMethodSymbol;

        if (expression is MemberAccessExpressionSyntax memberAccess) {
            var target = BuildAccessString(memberAccess.Expression, ctx);
            var methodName = GetSimpleName(memberAccess.Name);
            var separator = symbol switch
            {
                IMethodSymbol { MethodKind: MethodKind.ReducedExtension } => ":",
                { IsStatic: true } => ".",
                _ => ":",
            };

            if (symbol is IMethodSymbol { MethodKind: MethodKind.ReducedExtension } reducedExtension && reducedExtension.ReducedFrom is { } original)
            {
                methodName = original.Name;
            }

            return $"{target}{separator}{methodName}";
        }

        if (expression is IdentifierNameSyntax identifier) {
            if (symbol is null) {
                return identifier.Identifier.ValueText;
            }

            if (symbol is IMethodSymbol { MethodKind: MethodKind.ReducedExtension, ReducedFrom: { } originalMethod })
            {
                return $"self:{originalMethod.Name}";
            }

            if (symbol.ContainingType is not null && symbol.IsStatic) {
                return $"{BuildTypeAccess(symbol.ContainingType, ctx)}.{symbol.Name}";
            }

            if (!symbol.IsStatic) {
                return symbol.MethodKind == MethodKind.LocalFunction
                    ? symbol.Name
                    : $"self:{symbol.Name}";
            }
        }

        if (expression is ThisExpressionSyntax) {
            return symbol is { IsStatic: false } ? $"self:{symbol!.Name}" : symbol?.Name ?? "";
        }

        return BuildAccessString(expression, ctx);
    }

    internal static string BuildAccessString(ExpressionSyntax syntax, TranspilationContext ctx) {
        return syntax switch {
            IdentifierNameSyntax identifier => BuildNameFromSymbol(ctx.Semantics.GetSymbolInfo(identifier).Symbol, identifier.Identifier.ValueText, ctx),
            ThisExpressionSyntax => "self",
            MemberAccessExpressionSyntax memberAccess => BuildMemberAccessString(memberAccess, ctx),
            InvocationExpressionSyntax invocation => BuildInvocationAccessString(invocation, ctx),
            _ => syntax.ToString(),
        };
    }

    private static string BuildNameFromSymbol(ISymbol? symbol, string fallback, TranspilationContext ctx) {
        if (symbol is not null && ctx.TryGetSymbolAlias(symbol, out var alias))
        {
            return alias!;
        }

        if (symbol is null) return fallback;

        return symbol switch {
            IMethodSymbol methodSymbol => methodSymbol.IsStatic
                ? $"{BuildTypeAccess(methodSymbol.ContainingType, ctx)}.{methodSymbol.Name}"
                : $"self:{methodSymbol.Name}",
            IFieldSymbol fieldSymbol => fieldSymbol.IsStatic
                ? $"{BuildTypeAccess(fieldSymbol.ContainingType, ctx)}.{fieldSymbol.Name}"
                : $"self.{fieldSymbol.Name}",
            IPropertySymbol propertySymbol => propertySymbol.IsStatic
                ? $"{BuildTypeAccess(propertySymbol.ContainingType, ctx)}.{propertySymbol.Name}"
                : $"self.{propertySymbol.Name}",
            ILocalSymbol localSymbol => localSymbol.Name,
            IParameterSymbol parameterSymbol => parameterSymbol.Name,
            INamedTypeSymbol typeSymbol => BuildTypeAccess(typeSymbol, ctx),
            _ => symbol.Name,
        };
    }

    private static string BuildTypeAccess(INamedTypeSymbol typeSymbol, TranspilationContext ctx)
    {
        ctx.AddDependency(typeSymbol);
        return ctx.GetTypeName(typeSymbol);
    }

    internal static string GetSimpleName(SimpleNameSyntax simpleName) {
        return simpleName switch {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            _ => simpleName.ToString(),
        };
    }

    private static string BuildMemberAccessString(MemberAccessExpressionSyntax memberAccess, TranspilationContext ctx) {
        var left = BuildAccessString(memberAccess.Expression, ctx);
        var right = GetSimpleName(memberAccess.Name);
        return $"{left}.{right}";
    }

    private static string BuildInvocationAccessString(InvocationExpressionSyntax invocation, TranspilationContext ctx) {
        var callName = BuildCallName(invocation.Expression, ctx);
        var arguments = invocation.ArgumentList?.Arguments
            .Select(argument => RenderExpression(BuildFromSyntax(argument.Expression, ctx)))
            .ToArray() ?? Array.Empty<string>();

        return arguments.Length == 0
            ? $"{callName}()"
            : $"{callName}({string.Join(", ", arguments)})";
    }

    private static string RenderExpression(Expression expression) {
        var renderer = new RendererWalker();
        var chunk = new Chunk { Block = Block.Empty() };
        chunk.Block.AddStatement(Return.FromExpressions([expression]));

        var rendered = renderer.Render(chunk);
        var firstLine = rendered.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        const string prefix = "return ";
        return firstLine.StartsWith(prefix, StringComparison.Ordinal) ? firstLine[prefix.Length..] : firstLine;
    }
    private static bool ShouldUseLegacyCtor(ITypeSymbol? symbol)
    {
        return symbol?.TypeKind == TypeKind.Array;
    }

    internal static Expression HandleImplicitObjectCreationExpressionLegacy(ImplicitObjectCreationExpressionSyntax syntax, TranspilationContext ctx)
    {
        var typeInfo = ctx.Semantics.GetTypeInfo(syntax);
        var typeSymbol = typeInfo.Type;
        
        // Logger.Info($"ImplicitCreation: {typeSymbol?.Name ?? "null"}");

        if (ShouldUseLegacyCtor(typeSymbol))
        {
            return FunctionCall.Basic("table.create", NumberExpression.From(0));
        }

        if (IsDictionaryType(typeSymbol)) {
             return FunctionCall.Basic("Dictionary.new"); // Ensure Dictionary uses new
        }

        var namedTypeSymbol = typeSymbol as INamedTypeSymbol;
        if (namedTypeSymbol != null)
        {
             // Logger.Info($"Adding dependency for implicit creation: {namedTypeSymbol.Name}");
             ctx.AddDependency(namedTypeSymbol);
        }
        var typeName = namedTypeSymbol != null ? BuildTypeAccess(namedTypeSymbol, ctx) : (namedTypeSymbol?.ToDisplayString() ?? "Anonymous");

        var arguments = syntax.ArgumentList?.Arguments
            .Select(argument => BuildFromSyntax(argument.Expression, ctx))
            .ToArray() ?? Array.Empty<Expression>();

        return FunctionCallAst.Basic($"{typeName}.new", arguments);
    }

internal static Expression HandleObjectCreationExpressionLegacy(ObjectCreationExpressionSyntax syntax, TranspilationContext ctx) {
        var typeInfo = ctx.Semantics.GetTypeInfo(syntax);
        var typeSymbol = typeInfo.Type;

        if (syntax.Initializer is not null) {
            if (IsDictionaryType(typeSymbol)) {
                return BuildDictionaryInitializer(syntax.Initializer, ctx);
            }

            return BuildTableConstructor(syntax.Initializer, ctx);
        }



        var namedTypeSymbol = typeSymbol as INamedTypeSymbol;
        if (namedTypeSymbol != null)
        {
             ctx.AddDependency(namedTypeSymbol);
        }
        var typeName = namedTypeSymbol != null ? BuildTypeAccess(namedTypeSymbol, ctx) : syntax.Type.ToString();

        var arguments = syntax.ArgumentList?.Arguments
            .Select(argument => BuildFromSyntax(argument.Expression, ctx))
            .ToArray() ?? Array.Empty<Expression>();

        return FunctionCallAst.Basic($"{typeName}.new", arguments);
    }

    internal static Expression BuildObjectCreationWithoutInitializer(ObjectCreationExpressionSyntax syntax, TranspilationContext ctx)
    {
        var typeInfo = ctx.Semantics.GetTypeInfo(syntax);
        var typeSymbol = typeInfo.Type as INamedTypeSymbol;
        if (typeSymbol != null)
        {
             ctx.AddDependency(typeSymbol);
        }
        var typeName = typeSymbol != null ? BuildTypeAccess(typeSymbol, ctx) : syntax.Type.ToString();

        var arguments = syntax.ArgumentList?.Arguments
            .Select(argument => BuildFromSyntax(argument.Expression, ctx))
            .ToArray() ?? Array.Empty<Expression>();

        return FunctionCallAst.Basic($"{typeName}.new", arguments);
    }

    internal static Expression BuildImplicitObjectCreationWithoutInitializer(
        ImplicitObjectCreationExpressionSyntax syntax,
        ITypeSymbol? typeSymbol,
        TranspilationContext ctx)
    {
        var namedTypeSymbol = typeSymbol as INamedTypeSymbol;
        if (namedTypeSymbol != null)
        {
             ctx.AddDependency(namedTypeSymbol);
        }
        var typeName = namedTypeSymbol != null ? BuildTypeAccess(namedTypeSymbol, ctx) : (namedTypeSymbol?.ToDisplayString() ?? "Anonymous");

        var arguments = syntax.ArgumentList?.Arguments
            .Select(argument => BuildFromSyntax(argument.Expression, ctx))
            .ToArray() ?? Array.Empty<Expression>();

        return FunctionCallAst.Basic($"{typeName}.new", arguments);
    }

    internal static Expression BuildPropertyGetterCall(ExpressionSyntax? ownerSyntax, IPropertySymbol propertySymbol, TranspilationContext ctx) {
        var callName = BuildPropertyCallName(ownerSyntax, propertySymbol, ctx, isSetter: false);
        return FunctionCallAst.Basic(callName);
    }

    internal static FunctionCallAst BuildPropertySetterCall(ExpressionSyntax leftSyntax, IPropertySymbol propertySymbol, ExpressionSyntax valueSyntax, TranspilationContext ctx) {
        var ownerSyntax = leftSyntax switch {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            _ => null,
        };

        var callName = BuildPropertyCallName(ownerSyntax, propertySymbol, ctx, isSetter: true);
        var valueExpression = BuildFromSyntax(valueSyntax, ctx);
        return FunctionCallAst.Basic(callName, valueExpression);
    }

    private static string BuildPropertyCallName(ExpressionSyntax? ownerSyntax, IPropertySymbol propertySymbol, TranspilationContext ctx, bool isSetter) {
        var owner = ResolvePropertyOwner(ownerSyntax, propertySymbol, ctx);
        var separator = propertySymbol.IsStatic ? '.' : ':';
        var accessorPrefix = isSetter ? "Set" : "Get";
        return $"{owner}{separator}{accessorPrefix}{propertySymbol.Name}";
    }

    private static string ResolvePropertyOwner(ExpressionSyntax? ownerSyntax, IPropertySymbol propertySymbol, TranspilationContext ctx) {
        if (ownerSyntax is null) {
            return propertySymbol.IsStatic ? ctx.GetTypeName(propertySymbol.ContainingType) : "self";
        }

        if (ownerSyntax is ThisExpressionSyntax) {
            return "self";
        }

        return BuildAccessString(ownerSyntax, ctx);
    }

    private static bool TryBuildSliceLengthExpression(
        MemberAccessExpressionSyntax syntax,
        IPropertySymbol propertySymbol,
        TranspilationContext ctx,
        out Expression expression)
    {
        expression = null!;

        if (propertySymbol.Name is not ("Count" or "Length"))
        {
            return false;
        }

        if (syntax.Expression is not IdentifierNameSyntax identifier)
        {
            return false;
        }

        var identifierSymbol = ctx.Semantics.GetSymbolInfo(identifier).Symbol;
        ctx.TryGetSymbolAlias(identifierSymbol, out var aliasName);
        var sliceName = aliasName ?? identifier.Identifier.ValueText;

        if (!ctx.IsListSliceVariable(sliceName))
        {
            return false;
        }

        Expression operand;
        if (aliasName is not null)
        {
            operand = SymbolExpression.FromString(aliasName);
        }
        else
        {
            operand = BuildFromSyntax(syntax.Expression, ctx);
        }

        expression = new UnaryOperatorExpression
        {
            Op = UnOp.Length,
            Operand = operand,
        };
        return true;
    }

}
