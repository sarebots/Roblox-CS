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
        SyntaxKind.IsExpression,
        SyntaxKind.AsExpression,
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

        var leftExpression = context.BuildExpression(binary.Left);
        var rightExpression = context.BuildExpression(binary.Right);

        if (binary.IsKind(SyntaxKind.IsExpression))
        {
             // x is T -> TypeCheck(x, T)
             // We need a runtime helper for this: CS.is(x, T)
             // For now, let's emit a placeholder call.
             // TODO: Implement proper type checking logic (primitives vs classes vs Roblox types).
             return FunctionCallAst.Basic("CS.is", leftExpression, rightExpression);
        }
        
        if (binary.IsKind(SyntaxKind.AsExpression))
        {
             // x as T -> CS.as(x, T)
             // Helper: function(x, T) return CS.is(x, T) and x or nil end
             return FunctionCallAst.Basic("CS.as", leftExpression, rightExpression);
        }

        var mappedOperator = StandardUtility.GetMappedOperator(binary.OperatorToken.Text);
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
