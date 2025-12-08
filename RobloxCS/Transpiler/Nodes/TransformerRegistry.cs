using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST.Statements;
using RobloxCS.TranspilerV2.Nodes.Common;
using RobloxCS.TranspilerV2.Nodes.Declarations;
using RobloxCS.TranspilerV2.Nodes.Expressions;
using RobloxCS.TranspilerV2.Nodes.Statements;

namespace RobloxCS.TranspilerV2.Nodes;

internal static class TransformerRegistry
{
    private static readonly ConcurrentDictionary<SyntaxKind, ExpressionTransformer> ExpressionTransformers = new();
    private static readonly ConcurrentDictionary<SyntaxKind, StatementTransformer> StatementTransformers = new();
    private static readonly ConcurrentDictionary<SyntaxKind, Func<TranspilationContext, MemberDeclarationSyntax, IEnumerable<Statement>>> DeclarationTransformers = new();

    static TransformerRegistry()
    {
        ArrayCreationExpressionTransformer.Register();
        AssignmentExpressionTransformer.Register();
        AwaitExpressionTransformer.Register();
        BinaryExpressionTransformer.Register();
        CollectionExpressionTransformer.Register();
        ConditionalAccessExpressionTransformer.Register();
        ElementAccessExpressionTransformer.Register();
        LiteralExpressionTransformer.Register();
        InvocationExpressionTransformer.Register();
        ObjectCreationExpressionTransformer.Register();
        MethodGroupExpressionTransformer.Register();
        SwitchExpressionTransformer.Register();
        UnaryExpressionTransformer.Register();
        DoStatementTransformer.Register();
        ForStatementTransformer.Register();
        ForEachStatementTransformer.Register();
        IfStatementTransformer.Register();
        UsingStatementTransformer.Register();
        SwitchStatementTransformer.Register();
        TryStatementTransformer.Register();
        WhileStatementTransformer.Register();
        YieldStatementTransformer.Register();

        EnumDeclarationTransformer.Register();
        RecordDeclarationTransformer.Register();
        ClassDeclarationTransformer.Register();
        StructDeclarationTransformer.Register();
        InterfaceDeclarationTransformer.Register();
    }

    public static void RegisterExpressionTransformer(SyntaxKind syntaxKind, ExpressionTransformer transformer)
    {
        ExpressionTransformers[syntaxKind] = transformer;
    }

    public static void RegisterStatementTransformer(SyntaxKind syntaxKind, StatementTransformer transformer)
    {
        StatementTransformers[syntaxKind] = transformer;
    }

    public static bool TryGetExpressionTransformer(SyntaxKind syntaxKind, out ExpressionTransformer? transformer)
    {
        return ExpressionTransformers.TryGetValue(syntaxKind, out transformer);
    }

    public static bool TryGetStatementTransformer(SyntaxKind syntaxKind, out StatementTransformer? transformer)
    {
        return StatementTransformers.TryGetValue(syntaxKind, out transformer);
    }

    public static void RegisterDeclarationTransformer<TNode>(
        SyntaxKind syntaxKind,
        Func<TranspilationContext, TNode, IEnumerable<Statement>> transformer)
        where TNode : MemberDeclarationSyntax
    {
        DeclarationTransformers[syntaxKind] = (context, node) => transformer(context, (TNode)node);
    }

    public static bool TryGetDeclarationTransformer(
        SyntaxKind syntaxKind,
        out Func<TranspilationContext, MemberDeclarationSyntax, IEnumerable<Statement>>? transformer)
    {
        return DeclarationTransformers.TryGetValue(syntaxKind, out transformer);
    }
}
