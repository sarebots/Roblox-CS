using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Statements;

namespace RobloxCS.TranspilerV2.Nodes;

internal delegate Statement StatementTransformer(TranspilationContext context, StatementSyntax syntax);
