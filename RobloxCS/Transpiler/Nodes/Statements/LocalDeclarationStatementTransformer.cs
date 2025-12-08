using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Statements;
using RobloxCS.TranspilerV2.Builders;

using RobloxCS.AST.Expressions;

namespace RobloxCS.TranspilerV2.Nodes.Statements;

internal static class LocalDeclarationStatementTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterStatementTransformer(SyntaxKind.LocalDeclarationStatement, Transform);
    }

    private static Statement Transform(TranspilationContext context, StatementSyntax node)
    {
        var localDeclaration = (LocalDeclarationStatementSyntax)node;
        var declaration = localDeclaration.Declaration;
        
        var names = new List<SymbolExpression>();
        var values = new List<Expression>();
        var types = new List<AST.Types.TypeInfo>();
        
        foreach (var variable in declaration.Variables)
        {
            names.Add(SymbolExpression.FromString(variable.Identifier.Text));
            
            if (variable.Initializer != null)
            {
                values.Add(ExpressionBuilder.BuildFromSyntax(variable.Initializer.Value, context));
            }
            else
            {
                values.Add(SymbolExpression.FromString("nil"));
            }
            
            // TODO: Add type info
            types.Add(AST.Types.BasicTypeInfo.FromString("any"));
        }
        
        return new LocalAssignment
        {
            Names = names,
            Expressions = values,
            Types = types
        };
    }
}
