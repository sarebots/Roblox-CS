using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.Shared;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

using FunctionCallAst = RobloxCS.AST.Expressions.FunctionCall;
using RoslynTypeInfo = Microsoft.CodeAnalysis.TypeInfo;

internal static class BinaryExpressionTransformer
{
    private static readonly SyntaxKind[] SupportedKinds =
    {
        SyntaxKind.AddExpression,
        SyntaxKind.SubtractExpression,
        SyntaxKind.MultiplyExpression,
        SyntaxKind.DivideExpression,
        SyntaxKind.ModuloExpression,
        SyntaxKind.LeftShiftExpression,
        SyntaxKind.RightShiftExpression,
        SyntaxKind.BitwiseAndExpression,
        SyntaxKind.BitwiseOrExpression,
        SyntaxKind.ExclusiveOrExpression,
        SyntaxKind.GreaterThanExpression,
        SyntaxKind.GreaterThanOrEqualExpression,
        SyntaxKind.LessThanExpression,
        SyntaxKind.LessThanOrEqualExpression,
        SyntaxKind.EqualsExpression,
        SyntaxKind.NotEqualsExpression,
        SyntaxKind.LogicalAndExpression,
        SyntaxKind.LogicalOrExpression,
    };

    public static void Register()
    {
        foreach (var kind in SupportedKinds)
        {
            TransformerRegistry.RegisterExpressionTransformer(kind, Transform);
        }
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax syntax)
    {
        if (syntax is not BinaryExpressionSyntax binary)
        {
            return context.BuildExpressionWithoutTransformers(syntax);
        }

        var mappedOperator = StandardUtility.GetMappedOperator(binary.OperatorToken.Text);
        var leftExpression = context.BuildExpression(binary.Left);
        var rightExpression = context.BuildExpression(binary.Right);

        var bit32Method = StandardUtility.GetBit32MethodName(mappedOperator);
        if (bit32Method is not null)
        {
            return FunctionCallAst.Basic($"bit32.{bit32Method}", leftExpression, rightExpression);
        }

        var binOp = SyntaxUtilities.SyntaxTokenToBinOp(binary.OperatorToken);
        if (ShouldUseConcatenation(binOp, context.SemanticModel.GetTypeInfo(binary.Left), context.SemanticModel.GetTypeInfo(binary.Right)))
        {
            binOp = BinOp.TwoDots;
        }

        return new BinaryOperatorExpression
        {
            Left = leftExpression,
            Right = rightExpression,
            Op = binOp,
        };
    }

    private static bool ShouldUseConcatenation(BinOp op, RoslynTypeInfo leftTypeInfo, RoslynTypeInfo rightTypeInfo)
    {
        if (op != BinOp.Plus)
        {
            return false;
        }

        return IsStringLike(leftTypeInfo) || IsStringLike(rightTypeInfo);
    }

    private static bool IsStringLike(RoslynTypeInfo typeInfo)
    {
        return IsStringLike(typeInfo.Type) || IsStringLike(typeInfo.ConvertedType);
    }

    private static bool IsStringLike(ITypeSymbol? symbol)
    {
        if (symbol is null)
        {
            return false;
        }

        return symbol.SpecialType is SpecialType.System_String or SpecialType.System_Char;
    }
}
