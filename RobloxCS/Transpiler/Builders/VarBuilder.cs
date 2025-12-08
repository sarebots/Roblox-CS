using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;

namespace RobloxCS.TranspilerV2.Builders;

public class VarBuilder {
    public static Var BuildFromExpressionSyntax(ExpressionSyntax expr, TranspilationContext ctx) {
        return expr switch {
            IdentifierNameSyntax nameSyntax => HandleIdentifierNameSyntax(nameSyntax, ctx),
            MemberAccessExpressionSyntax memberAccessSyntax => HandleMemberAccessSyntax(memberAccessSyntax, ctx),
            ElementAccessExpressionSyntax elementAccessSyntax => HandleElementAccessSyntax(elementAccessSyntax, ctx),

            _ => throw new NotSupportedException($"Unsupported expression in assignment: {expr.Kind()}"),
        };
    }

    private static Var HandleIdentifierNameSyntax(IdentifierNameSyntax nameSyntax, TranspilationContext ctx) {
        if (nameSyntax.SyntaxTree != ctx.Root.SyntaxTree)
        {
            return VarName.FromString(nameSyntax.Identifier.ValueText);
        }

        var symbol = ctx.Semantics.GetSymbolInfo(nameSyntax).Symbol;
        if (symbol is null) throw new Exception($"Semantics failed to get symbol info for {nameSyntax.Identifier.ValueText}");

        return symbol switch {
            IFieldSymbol fieldSymbol => fieldSymbol.IsStatic
                ? VarName.FromString($"{ctx.GetTypeName(fieldSymbol.ContainingType)}.{fieldSymbol.Name}")
                : VarName.FromString($"self.{fieldSymbol.Name}"),
            IPropertySymbol propertySymbol => propertySymbol.IsStatic
                ? VarName.FromString($"{ctx.GetTypeName(propertySymbol.ContainingType)}.{propertySymbol.Name}")
                : VarName.FromString($"self.{propertySymbol.Name}"),
            IEventSymbol eventSymbol => eventSymbol.IsStatic
                ? VarName.FromString($"{ctx.GetTypeName(eventSymbol.ContainingType)}.{eventSymbol.Name}")
                : VarName.FromString($"self.{eventSymbol.Name}"),
            ILocalSymbol localSymbol => VarName.FromString(localSymbol.Name),
            IParameterSymbol parameterSymbol => VarName.FromString(parameterSymbol.Name),
            IDiscardSymbol => VarName.FromString(RobloxCS.Luau.AstUtility.DiscardName.Text),

            _ => throw new NotSupportedException($"Symbol of type {symbol.GetType().Name} is not supported."),
        };
    }

    private static Var HandleMemberAccessSyntax(MemberAccessExpressionSyntax memberAccessSyntax, TranspilationContext ctx) {
        var accessString = ExpressionBuilder.BuildAccessString(memberAccessSyntax, ctx);
        return VarName.FromString(accessString);
    }

    private static Var HandleElementAccessSyntax(ElementAccessExpressionSyntax syntax, TranspilationContext ctx) {
        var expression = ExpressionBuilder.BuildFromSyntax(syntax, ctx);
        return VarExpression.FromExpression(expression);
    }
}
