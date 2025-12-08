using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Statements;
using RobloxCS.TranspilerV2.Scoping;

namespace RobloxCS.TranspilerV2;

public sealed class TranspilationContext {
    public TranspilerOptions Options { get; }
    public CSharpCompilation Compilation { get; }
    public SemanticModel Semantics { get; }
    public CompilationUnitSyntax Root { get; }
    public Block RootBlock { get; } = Block.Empty();
    public HashSet<ISymbol> AsyncSymbols { get; } = new(SymbolEqualityComparer.Default);
    internal Stack<LoopInfo> LoopStack { get; } = new();
    internal Dictionary<string, int> TempIdentifierCounters { get; } = new(StringComparer.Ordinal);
    internal TypeDeclarationScope TypeDeclarations { get; } = new();
    private readonly HashSet<string> _listSliceVariables = new(StringComparer.Ordinal);
    private readonly Dictionary<ISymbol, Stack<string>> _symbolAliases = new(SymbolEqualityComparer.Default);
    private readonly List<Statement> _pendingPrerequisites = new();
    private readonly Dictionary<string, LocalAssignment> _typePredeclarationStatements = new(StringComparer.Ordinal);
    private readonly List<string> _typePredeclarationOrder = new();
    private readonly HashSet<string> _typePredeclarations = new(StringComparer.Ordinal);
    private readonly Stack<GeneratorScope> _generatorScopes = new();
    private bool _signalImportAdded;

    public string AllocateTempName(params string[] parts)
    {
        var components = parts.Length == 0
            ? new[] { "temp" }
            : parts.Select(part => string.IsNullOrWhiteSpace(part) ? "temp" : part.Trim()).ToArray();

        var normalized = string.Join("_", components).Replace(' ', '_');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "temp";
        }

        var baseName = normalized.StartsWith("_", StringComparison.Ordinal)
            ? normalized
            : $"_{normalized}";

        if (!TempIdentifierCounters.TryGetValue(baseName, out var count))
        {
            TempIdentifierCounters[baseName] = 1;
            return baseName;
        }

        var result = $"{baseName}_{count}";
        TempIdentifierCounters[baseName] = count + 1;
        return result;
    }

    public Scope CurrentScope => _scopes.Peek();

    private readonly Stack<Scope> _scopes = new();
    private readonly Stack<TryScopeInfo> _tryScopes = new();

    public TranspilationContext(TranspilerOptions options, CSharpCompilation compilation, CompilationUnitSyntax root, SemanticModel? semanticModel = null) {
        Options = options;
        Compilation = compilation;
        Root = root;
        Semantics = semanticModel ?? compilation.GetSemanticModel(root.SyntaxTree);
    }

    public void MarkAsync(ISymbol symbol) => AsyncSymbols.Add(symbol);

    public bool IsAsync(ISymbol symbol) => AsyncSymbols.Contains(symbol);
    public bool IsInsideLoop => LoopStack.Count > 0;

    public void PushScope() {
        var parent = _scopes.Count > 0 ? _scopes.Peek() : null;

        _scopes.Push(new Scope(parent));
    }

    public void PopScope() => _scopes.Pop();

    public void PushTryScope() => _tryScopes.Push(new TryScopeInfo());

    public TryScopeInfo PopTryScope() => _tryScopes.Count > 0 ? _tryScopes.Pop() : new TryScopeInfo();

    public TryScopeInfo CurrentTryScope => _tryScopes.Count > 0 ? _tryScopes.Peek() : new TryScopeInfo();

    public bool IsInsideTryScope => _tryScopes.Count > 0;

    public void MarkReturnEncountered()
    {
        if (_tryScopes.Count > 0)
        {
            _tryScopes.Peek().UsesReturn = true;
        }
    }

    public void MarkBreakEncountered()
    {
        if (_tryScopes.Count > 0)
        {
            _tryScopes.Peek().UsesBreak = true;
        }
    }

    public void MarkContinueEncountered()
    {
        if (_tryScopes.Count > 0)
        {
            _tryScopes.Peek().UsesContinue = true;
        }
    }

    public string GetTypeName(INamedTypeSymbol symbol) => TypeDeclarations.GetTypeName(symbol);

    public void AddPrerequisite(Statement statement)
    {
        if (statement is null) throw new ArgumentNullException(nameof(statement));
        _pendingPrerequisites.Add(statement);
    }

    public string EnsureTypePredeclaration(INamedTypeSymbol symbol)
    {
        var typeName = GetTypeName(symbol);
        EnsureTypePredeclaration(typeName);
        return typeName;
    }

    public void EnsureTypePredeclaration(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return;
        }

        if (!_typePredeclarations.Add(typeName))
        {
            return;
        }

        var statement = new LocalAssignment
        {
            Names = [SymbolExpression.FromString(typeName)],
            Expressions = [],
            Types = [],
        };

        _typePredeclarationStatements[typeName] = statement;
        _typePredeclarationOrder.Add(typeName);
    }


    public bool HasPrerequisites => _pendingPrerequisites.Count > 0;

    public IReadOnlyList<Statement> ConsumePrerequisites()
    {
        if (_pendingPrerequisites.Count == 0)
        {
            return Array.Empty<Statement>();
        }

        var result = _pendingPrerequisites.ToArray();
        _pendingPrerequisites.Clear();
        return result;
    }

    public bool TryConsumeTypePredeclaration(string typeName, out LocalAssignment? statement)
    {
        statement = null;

        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        if (!_typePredeclarationStatements.TryGetValue(typeName, out var predeclaration))
        {
            return false;
        }

        _typePredeclarationStatements.Remove(typeName);
        _typePredeclarationOrder.Remove(typeName);
        statement = predeclaration;
        return true;
    }

    public void AppendPrerequisites(Block block)
    {
        foreach (var prerequisite in ConsumePrerequisites())
        {
            block.AddStatement(prerequisite);
        }
    }

    public void AppendPrerequisites(ICollection<Statement> statements)
    {
        foreach (var prerequisite in ConsumePrerequisites())
        {
            statements.Add(prerequisite);
        }
    }

    public void RegisterListSliceVariable(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _listSliceVariables.Add(name);
        }
    }

    public bool IsListSliceVariable(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && _listSliceVariables.Contains(name);
    }

    public IDisposable PushSymbolAlias(ISymbol symbol, string alias)
    {
        if (symbol is null || string.IsNullOrWhiteSpace(alias))
        {
            return EmptyDisposable.Instance;
        }

        if (!_symbolAliases.TryGetValue(symbol, out var stack))
        {
            stack = new Stack<string>();
            _symbolAliases[symbol] = stack;
        }

        stack.Push(alias);
        return new SymbolAliasScope(this, symbol);
    }

    internal void PopSymbolAlias(ISymbol symbol)
    {
        if (!_symbolAliases.TryGetValue(symbol, out var stack) || stack.Count == 0)
        {
            return;
        }

        stack.Pop();
        if (stack.Count == 0)
        {
            _symbolAliases.Remove(symbol);
        }
    }

    public bool TryGetSymbolAlias(ISymbol? symbol, out string? alias)
    {
        alias = null;

        if (symbol is null)
        {
            return false;
        }

        if (_symbolAliases.TryGetValue(symbol, out var stack) && stack.Count > 0)
        {
            alias = stack.Peek();
            return true;
        }

        return false;
    }

    public void PushGeneratorScope(SymbolExpression stateExpression, bool hasBreakHandler)
    {
        _generatorScopes.Push(new GeneratorScope(stateExpression, hasBreakHandler));
    }

    public void PopGeneratorScope()
    {
        if (_generatorScopes.Count > 0)
        {
            _generatorScopes.Pop();
        }
    }

    public bool TryGetGeneratorScope(out GeneratorScope scope)
    {
        if (_generatorScopes.Count > 0)
        {
            scope = _generatorScopes.Peek();
            return true;
        }

        scope = default;
        return false;
    }

    public Chunk ToChunk() {
        PrependTypePredeclarations();
        NormalizeReturnStatements();
        return new Chunk { Block = RootBlock };
    }

    public void Add(params Statement[] statements) {
        foreach (var s in statements) RootBlock.AddStatement(s);
    }

    public void EnsureSignalImport()
    {
        if (_signalImportAdded)
        {
            return;
        }

        var requireCall = FunctionCall.Basic("require", SymbolExpression.FromString("rbxcs_include.GoodSignal"));
        var assignment = new LocalAssignment
        {
            Names = [SymbolExpression.FromString("Signal")],
            Expressions = [requireCall],
            Types = [],
        };

        RootBlock.Statements.Insert(0, assignment);
        _signalImportAdded = true;
    }

    private void NormalizeReturnStatements() {
        var nilReturnIndices = RootBlock.Statements
            .Select((statement, index) => (statement, index))
            .Where(tuple => tuple.statement is Return ret
                && ret.Returns.Count == 1
                && ret.Returns[0] is SymbolExpression symbol
                && symbol.Value == "nil")
            .Select(tuple => tuple.index)
            .ToList();

        if (nilReturnIndices.Count == 0) {
            RootBlock.AddStatement(Return.FromExpressions([SymbolExpression.FromString("nil")]));
            return;
        }

        for (var i = 0; i < nilReturnIndices.Count - 1; i++) {
            var index = nilReturnIndices[i] - i; // adjust for prior removals
            RootBlock.Statements.RemoveAt(index);
        }
    }

    private void PrependTypePredeclarations()
    {
        if (_typePredeclarationOrder.Count == 0)
        {
            return;
        }

        var remaining = new List<Statement>();
        foreach (var typeName in _typePredeclarationOrder)
        {
            if (_typePredeclarationStatements.TryGetValue(typeName, out var predeclaration))
            {
                remaining.Add(predeclaration);
            }
        }

        _typePredeclarationOrder.Clear();
        _typePredeclarationStatements.Clear();

        if (remaining.Count == 0)
        {
            return;
        }

        RootBlock.Statements.InsertRange(0, remaining);
    }

    private sealed class SymbolAliasScope : IDisposable
    {
        private readonly TranspilationContext _context;
        private readonly ISymbol _symbol;
        private bool _disposed;

        public SymbolAliasScope(TranspilationContext context, ISymbol symbol)
        {
            _context = context;
            _symbol = symbol;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _context.PopSymbolAlias(_symbol);
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }

    public sealed class TryScopeInfo
    {
        public bool UsesReturn { get; set; }
        public bool UsesBreak { get; set; }
        public bool UsesContinue { get; set; }

        public bool HasControlFlow => UsesReturn || UsesBreak || UsesContinue;
    }

    public readonly record struct GeneratorScope(SymbolExpression StateExpression, bool HasBreakHandler);
}
