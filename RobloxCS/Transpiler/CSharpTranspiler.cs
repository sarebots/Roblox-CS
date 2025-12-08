using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Prefixes;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Suffixes;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2.Builders;
using RobloxCS.TranspilerV2.Nodes;

namespace RobloxCS.TranspilerV2;

public sealed class CSharpTranspiler : CSharpSyntaxWalker {
    public TranspilationContext Ctx { get; }

    public CSharpTranspiler(TranspilationContext context) {
        Ctx = context;
    }

    public CSharpTranspiler(TranspilerOptions options, CSharpCompilation compilation, CompilationUnitSyntax root, SemanticModel? semantics = null)
        : this(new TranspilationContext(options, compilation, root, semantics))
    {
    }

    public Chunk Transpile() {
        Logger.Info("Starting transpilation");
        var watch = Stopwatch.StartNew();

        PredeclareTypes(Ctx.Root);
        Visit(Ctx.Root);

        watch.Stop();
        Logger.Info($"Finished transpiling in {watch.ElapsedMilliseconds}ms");

        return Ctx.ToChunk();
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node) => HandleTypeDeclaration(node);

    public override void VisitStructDeclaration(StructDeclarationSyntax node) => HandleTypeDeclaration(node);

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) => HandleTypeDeclaration(node);

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node) => HandleTypeDeclaration(node);

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node) => HandleTypeDeclaration(node);

    public override void VisitFieldDeclaration(FieldDeclarationSyntax node) {
        // Fields declared inside types are handled by the enclosing type transformer.
        if (!string.IsNullOrEmpty(Ctx.TypeDeclarations.GetCurrentTypeName()))
        {
            return;
        }

        Logger.Warn($"Skipping emission for top-level field group with {node.Declaration.Variables.Count} variable(s).");
    }

    private void HandleTypeDeclaration(BaseTypeDeclarationSyntax node)
    {
        if (Ctx.Semantics.GetDeclaredSymbol(node) is not INamedTypeSymbol typeSymbol)
        {
            // Fall back to the default walker implementation when we don't have a symbol.
            DispatchToBase(node);
            return;
        }

        using (Ctx.TypeDeclarations.Push(typeSymbol))
        {
            if (TransformerRegistry.TryGetDeclarationTransformer(node.Kind(), out var declarationTransformer) && declarationTransformer is not null)
            {
                foreach (var stmt in declarationTransformer(Ctx, node))
                {
                    Ctx.Add(stmt);
                }
            }
            else if (node is ClassDeclarationSyntax classDeclaration)
            {
                var classStatements = ClassBuilder.Build(classDeclaration, typeSymbol, Ctx);
                foreach (var stmt in classStatements)
                {
                    Ctx.Add(stmt);
                }
            }
            // Walk child nodes using the base walker implementation for this concrete type,
            // avoiding recursive re-entry into HandleTypeDeclaration on the same node.
            DispatchToBase(node);
        }
    }

    private void DispatchToBase(BaseTypeDeclarationSyntax node)
    {
        switch (node)
        {
            case ClassDeclarationSyntax classDeclaration:
                base.VisitClassDeclaration(classDeclaration);
                break;
            case StructDeclarationSyntax structDeclaration:
                base.VisitStructDeclaration(structDeclaration);
                break;
            case InterfaceDeclarationSyntax interfaceDeclaration:
                base.VisitInterfaceDeclaration(interfaceDeclaration);
                break;
        }
    }

    private void PredeclareTypes(SyntaxNode node)
    {
        foreach (var child in node.ChildNodes())
        {
            switch (child)
            {
                case NamespaceDeclarationSyntax namespaceDeclaration:
                    PredeclareTypes(namespaceDeclaration);
                    break;
                case FileScopedNamespaceDeclarationSyntax fileScopedNamespace:
                    PredeclareTypes(fileScopedNamespace);
                    break;
                case ClassDeclarationSyntax or StructDeclarationSyntax or RecordDeclarationSyntax:
                    if (child is BaseTypeDeclarationSyntax typeDeclaration
                        && Ctx.Semantics.GetDeclaredSymbol(typeDeclaration) is INamedTypeSymbol typeSymbol)
                    {
                        using (Ctx.TypeDeclarations.Push(typeSymbol))
                        {
                            Ctx.EnsureTypePredeclaration(typeSymbol);
                            PredeclareTypes(typeDeclaration);
                        }
                    }
                    break;
                case BaseTypeDeclarationSyntax otherType:
                    PredeclareTypes(otherType);
                    break;
            }
        }
    }
}
