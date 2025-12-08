using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace RobloxCS.TranspilerV2;

internal sealed class TypeDeclarationScope
{
    private readonly Stack<INamedTypeSymbol> _typeStack = new();

    public IDisposable Push(INamedTypeSymbol symbol)
    {
        _typeStack.Push(symbol);
        return new PopGuard(_typeStack);
    }

    public string GetCurrentTypeName()
    {
        if (_typeStack.Count == 0)
        {
            return string.Empty;
        }

        return BuildName(_typeStack.Reverse());
    }

    public string GetTypeName(INamedTypeSymbol symbol)
    {
        if (symbol is null)
        {
            return string.Empty;
        }

        return BuildName(GetSymbolChain(symbol));
    }

    public string SanitizeTypeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Type";
        }

        return Sanitize(name);
    }

    private static IEnumerable<INamedTypeSymbol> GetSymbolChain(INamedTypeSymbol symbol)
    {
        var stack = new Stack<INamedTypeSymbol>();
        var current = symbol;
        while (current != null)
        {
            stack.Push(current);
            current = current.ContainingType;
        }

        return stack;
    }

    private static string BuildName(IEnumerable<INamedTypeSymbol> symbols)
    {
        return string.Join("_", symbols.Select(s => Sanitize(s.Name)));
    }

    private static string Sanitize(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return "Type";
        }

        var buffer = new List<(char value, bool fromInvalid)>(identifier.Length);
        var lastWasUnderscore = false;

        foreach (var ch in identifier)
        {
            var isAllowed = char.IsLetterOrDigit(ch) || ch == '_';
            var normalized = isAllowed ? ch : '_';

            if (normalized == '_')
            {
                if (lastWasUnderscore)
                {
                    continue;
                }

                lastWasUnderscore = true;
                buffer.Add((normalized, fromInvalid: !isAllowed));
                continue;
            }

            lastWasUnderscore = false;
            buffer.Add((normalized, fromInvalid: false));
        }

        while (buffer.Count > 0 && buffer[0].value == '_' && buffer[0].fromInvalid)
        {
            buffer.RemoveAt(0);
        }

        while (buffer.Count > 0 && buffer[^1].value == '_' && buffer[^1].fromInvalid)
        {
            buffer.RemoveAt(buffer.Count - 1);
        }

        if (buffer.Count == 0)
        {
            return "Type";
        }

        if (buffer.All(entry => entry.value == '_'))
        {
            return "Type";
        }

        if (char.IsDigit(buffer[0].value))
        {
            buffer.Insert(0, ('_', fromInvalid: false));
        }

        return new string(buffer.Select(entry => entry.value).ToArray());
    }

    private sealed class PopGuard : IDisposable
    {
        private readonly Stack<INamedTypeSymbol> _stack;
        private bool _disposed;

        public PopGuard(Stack<INamedTypeSymbol> stack)
        {
            _stack = stack;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_stack.Count > 0)
            {
                _stack.Pop();
            }

            _disposed = true;
        }
    }
}
