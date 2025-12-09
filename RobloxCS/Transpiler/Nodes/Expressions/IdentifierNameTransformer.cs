using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class IdentifierNameTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.IdentifierName, Transform);
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax node)
    {
        var identifier = (IdentifierNameSyntax)node;
        var info = context.SemanticModel.GetSymbolInfo(identifier);
        var symbol = info.Symbol;

        if (symbol is null)
        {
            return SymbolExpression.FromString(identifier.Identifier.Text);
        }

        // Enum constant optimization
        if (symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum, HasConstantValue: true } enumField)
        {
             if (enumField.ConstantValue is int i) return NumberExpression.From(i);
             if (enumField.ConstantValue is long l) return NumberExpression.From((double)l);
             // Fallback
             return NumberExpression.From(Convert.ToDouble(enumField.ConstantValue));
        }

        bool isMember = symbol is IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol;

        if (symbol.IsStatic && isMember)
        {
            // If we are the right-hand side of a member access (e.g. Class.StaticMember),
            // we don't need to qualify it again.
            if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node)
            {
                return SymbolExpression.FromString(identifier.Identifier.Text);
            }

            // Implicit static access (e.g. inside the class).
            // We must qualify it with the class name for Luau.
            // Use GetTypeName from TranspilationContext to handle nested types correctly.
            var containerName = context.TranspilationContext.GetTypeName(symbol.ContainingType);
            
            return SymbolExpression.FromString($"{containerName}.{identifier.Identifier.Text}");
        }

        // Instance Members -> self.Member
        if (!symbol.IsStatic && isMember)
        {
             // If NOT right side of member access, it is implicit 'this'
             if (!(node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node))
             {
                 return SymbolExpression.FromString($"self.{identifier.Identifier.Text}");
             }
        }

        return SymbolExpression.FromString(identifier.Identifier.Text);
    }
}
