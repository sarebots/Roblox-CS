using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;

namespace RobloxCS.TranspilerV2.Nodes.Expressions;

internal static class InterpolatedStringExpressionTransformer
{
    public static void Register()
    {
        TransformerRegistry.RegisterExpressionTransformer(SyntaxKind.InterpolatedStringExpression, Transform);
    }

    private static Expression Transform(TransformContext context, ExpressionSyntax node)
    {
        var interpolatedString = (InterpolatedStringExpressionSyntax)node;
        
        var formatString = "";
        var args = new List<Expression>();
        
        foreach (var content in interpolatedString.Contents)
        {
            if (content is InterpolatedStringTextSyntax text)
            {
                formatString += text.TextToken.Text;
            }
            else if (content is InterpolationSyntax interpolation)
            {
                formatString += "%s"; // Simple string interpolation for now
                args.Add(context.BuildExpression(interpolation.Expression));
                // TODO: Handle alignment and format string
            }
        }
        
        var allArgs = new List<Expression> { StringExpression.FromString(formatString) };
        allArgs.AddRange(args);

        return FunctionCall.Basic("string.format", allArgs.ToArray());
    }
}
