using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Expressions;

namespace RobloxCS.TranspilerV2.Nodes;

internal delegate Expression? ExpressionTransformer(TransformContext context, ExpressionSyntax syntax);
