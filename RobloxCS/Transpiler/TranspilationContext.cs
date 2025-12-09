using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Statements;
using RobloxCS.TranspilerV2.Nodes;
using RobloxCS.TranspilerV2.Scoping;
using RobloxCS.Shared;

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
    private readonly HashSet<INamedTypeSymbol> _dependencies = new(SymbolEqualityComparer.Default);
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
        GenerateImports();
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

    public void AddDependency(INamedTypeSymbol symbol)
    {
        if (symbol.ContainingAssembly.Name != Compilation.AssemblyName)
        {
            // For now, only track dependencies within the same compilation
            // External references would need config mapping
             _dependencies.Add(symbol);
             return;
        }

        // Check if the symbol is defined in the current file options
        // This is a rough check. Ideally we compare syntax trees.
        if (symbol.Locations.Any(loc => loc.SourceTree == Root.SyntaxTree))
        {
            return;
        }

        _dependencies.Add(symbol);
    }

    private void GenerateImports()
    {
        if (Options.RojoProject is null) return;

        var currentFilePath = Root.SyntaxTree.FilePath;
        var imports = new Dictionary<string, string>(StringComparer.Ordinal); // ClassName -> Path

        // Always add RobloxCS.Runtime helpers if needed
        // For now we assume they are globally available or injected via defineGlobal? 
        // Actually, the user report says "unknown global Roblox". 
        // V1 likely handled this via implicit imports too.

        if (_signalImportAdded)
        {
            var includePath = "game:GetService(\"ReplicatedStorage\"):WaitForChild(\"include\")";
            var includeStmt = new LocalAssignment
            {
                Names = [SymbolExpression.FromString("rbxcs_include")],
                Expressions = [SymbolExpression.FromString(includePath)],
                Types = []
            };
            RootBlock.Statements.Insert(0, includeStmt);
        }

        foreach (var dep in _dependencies)
        {
            var className = Options.RojoProject.EmitLegacyScripts ? dep.Name : dep.Name; 
            if (imports.ContainsKey(className)) continue;

            string? requirePath = null;
            
            if (dep.ContainingNamespace?.ToString().StartsWith("Roblox") == true)
            {
                // Fallback for runtime types
                var runtimePath = "game:GetService(\"ReplicatedStorage\"):WaitForChild(\"RobloxCS.Runtime\")";
                requirePath = $"{runtimePath}:WaitForChild(\"{dep.Name}\")"; // Use WaitForChild for safety
            }
            else
            {
                 var loc = dep.Locations.FirstOrDefault(l => l.IsInSource);
                 if (loc != null)
                 {
                     var depPath = loc.SourceTree!.FilePath;
                     var currentPath = Root.SyntaxTree.FilePath;
                     
                     // Use relative path for internal dependencies
                     var currentDir = Path.GetDirectoryName(currentPath);
                     var depDir = Path.GetDirectoryName(depPath);

                     if (currentDir != null && depDir != null)
                     {
                         var relativePath = Path.GetRelativePath(currentDir, depDir);
                         var parts = relativePath.Split(Path.DirectorySeparatorChar);
                         
                         // Start at script.Parent (the container of the current script)
                         var builder = new System.Text.StringBuilder("script.Parent");
                         
                         foreach (var part in parts)
                         {
                             if (part == ".") continue;
                             if (part == "..") 
                             {
                                 builder.Append(".Parent");
                             }
                             else
                             {
                                 builder.Append($":WaitForChild(\"{part}\")"); // Use WaitForChild for folders for safety? Or just dot. 
                                 // Folders usually exist immediately in ReplicatedStorage, but WaitForChild is safer for streaming/loading order.
                                 // However, standard require usually uses dot access for siblings.
                                 // Let's use dot access for folders to be cleaner, WaitForChild for the final module?
                                 // Actually, usually dot access is fine for server/shared scripts.
                                 // But let's stick to dot for folders.
                                 // Wait, if it's "script.Parent.UI", that's fine.
                                 // But if I use names, I need to match valid identifier logic.
                                 // For now, dot access.
                                 // Correction: Use WaitForChild for cross-directory if safest, but messy.
                                 // Let's use ["Name"] or .Name.
                                 builder.Append($".{part}");
                             }
                         }
                         
                         // Append the module name. Use WaitForChild for the module itself as it might be a script not yet loaded?
                         // Server scripts load together. ModuleScripts too.
                         // But require(script.Parent.Module) is standard.
                         builder.Append($".{dep.Name}");
                         requirePath = builder.ToString();
                     }
                     else
                     {
                         // Fallback to Rojo resolution if relative path calculation fails (shouldn't happen for valid paths)
                         var robloxPath = RojoReader.ResolveInstancePath(Options.RojoProject, depPath);
                         if (robloxPath != null)
                         {
                              requirePath = robloxPath.StartsWith("game") ? robloxPath : $"game.{robloxPath}";
                         }
                     }
                 }
            }
            
            if (requirePath != null)
            {
                imports[className] = requirePath;
            }
        }

        // Emit import statements
        // We insert them at the top. 
        // If signal import was added, it inserted 'Signal', then we inserted 'rbxcs_include' at 0.
        // So 'rbxcs_include' is at 0, 'Signal' is at 1.
        // We want other imports after these.
        var index = _signalImportAdded ? 2 : 0;
        
        foreach (var kvp in imports.OrderBy(x => x.Key))
        {
            var call = FunctionCall.Basic("require", SymbolExpression.FromString(kvp.Value));
            var stmt = new LocalAssignment
            {
                Names = [SymbolExpression.FromString(kvp.Key)],
                Expressions = [call],
                Types = [],
            };
            RootBlock.Statements.Insert(index++, stmt);
        }
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
