using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Expressions;

namespace RobloxCS.TranspilerV2.Builders;

internal static class DefaultValueBuilder
{
    public static Expression? CreateDefaultValueExpression(ITypeSymbol typeSymbol, TranspilationContext ctx)
    {
        ExpressionSyntax? literal = typeSymbol.SpecialType switch
        {
            SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_IntPtr or SpecialType.System_UIntPtr => SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(0)),
            SpecialType.System_Single or SpecialType.System_Double => SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(0.0)),
            SpecialType.System_Decimal => SyntaxFactory.LiteralExpression(
                SyntaxKind.NumericLiteralExpression,
                SyntaxFactory.Literal(0m)),
            SpecialType.System_Boolean => SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression),
            SpecialType.System_Char => SyntaxFactory.LiteralExpression(
                SyntaxKind.CharacterLiteralExpression,
                SyntaxFactory.Literal('\0')),
            SpecialType.System_String => SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(string.Empty)),
            _ => null,
        };

        if (literal is null)
        {
            literal = typeSymbol.IsReferenceType
                ? SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
                : null;
        }

        return literal is null
            ? null
            : ExpressionBuilder.BuildFromSyntax(literal, ctx);
    }
}
