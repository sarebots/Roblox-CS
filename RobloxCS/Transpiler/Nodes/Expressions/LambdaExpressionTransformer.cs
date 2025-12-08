using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Statements;

using RobloxCS.AST.Parameters; // For NameParameter
using RobloxCS.AST.Types; // For TypeInfo, BasicTypeInfo
using RobloxCS.AST.Functions; // For FunctionBody
using RobloxCS.TranspilerV2.Builders; // For StatementBuilder

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class LambdaExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.SimpleLambdaExpression, Transform);
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.ParenthesizedLambdaExpression, Transform);
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax node)
    {
        var lambda = (LambdaExpressionSyntax)node;
        
        // (params) => body
        // function(params) body end
        
        var parameters = new List<AST.Parameters.Parameter>();
        var typeSpecifiers = new List<AST.Types.TypeInfo>();
        
        if (lambda is SimpleLambdaExpressionSyntax simple)
        {
            parameters.Add(AST.Parameters.NameParameter.FromString(simple.Parameter.Identifier.Text));
            typeSpecifiers.Add(AST.Types.BasicTypeInfo.FromString("any"));
        }
        else if (lambda is ParenthesizedLambdaExpressionSyntax parenthesized)
        {
            foreach (var param in parenthesized.ParameterList.Parameters)
            {
                parameters.Add(AST.Parameters.NameParameter.FromString(param.Identifier.Text));
                typeSpecifiers.Add(AST.Types.BasicTypeInfo.FromString("any"));
            }
        }
        
        Block bodyBlock;
        if (lambda.Body is BlockSyntax block)
        {
            var statements = new List<Statement>();
            foreach (var statement in block.Statements)
            {
                statements.Add(Builders.StatementBuilder.Transpile(statement, context.TranspilationContext));
            }
            bodyBlock = new Block { Statements = statements };
        }
        else if (lambda.Body is ExpressionSyntax expr)
        {
            // If body is an expression: x => x + 1
            // function(x) return x + 1 end
            var expression = context.BuildExpression(expr);
            bodyBlock = new Block { Statements = [new Return { Returns = [expression] }] };
        }
        else
        {
            // Should not happen for valid C# lambda
            bodyBlock = Block.Empty();
        }
        
        var functionBody = new AST.Functions.FunctionBody
        {
            Parameters = parameters,
            Body = bodyBlock,
            Generics = null,
            TypeSpecifiers = typeSpecifiers,
            ReturnType = AST.Types.BasicTypeInfo.FromString("any") // Inferred
        };
        
        return new AnonymousFunction { Body = functionBody };
    }
}
