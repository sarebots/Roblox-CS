using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace RobloxCS.Luau;

public class SymbolMetadata
{
    public IdentifierName? EventConnectionName { get; set; }
    public Dictionary<string, Dictionary<IMethodSymbol, int>>? MethodOverloads { get; set; }
    public HashSet<IMethodSymbol> AsyncMethods { get; } = new(SymbolEqualityComparer.Default);
    public HashSet<IMethodSymbol> GeneratorMethods { get; } = new(SymbolEqualityComparer.Default);
}

public static class SymbolMetadataManager
{
    private static readonly Dictionary<ISymbol, SymbolMetadata> _metadata = [];

    public static SymbolMetadata Get(ISymbol symbol)
    {
        if (symbol is null) {
            return new SymbolMetadata();
        }

        var metadata = _metadata.GetValueOrDefault(symbol);
        if (metadata == null) _metadata.Add(symbol, metadata = new SymbolMetadata());

        return metadata;
    }

    public static void Clear() => _metadata.Clear();
}
