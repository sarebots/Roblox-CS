using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RobloxCS.CLI;

internal sealed class ChangeAccumulator
{
    private readonly object _lock = new();
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(path);
        }
        catch
        {
            normalized = path;
        }

        lock (_lock)
        {
            _paths.Add(normalized);
        }
    }

    public bool TryTakeSummary(out string summary)
    {
        lock (_lock)
        {
            if (_paths.Count == 0)
            {
                summary = string.Empty;
                return false;
            }

            var sample = _paths
                .Take(5)
                .Select(path => Path.GetFileName(path) ?? path)
                .ToArray();

            var suffix = sample.Length > 0 ? $": {string.Join(", ", sample)}" : string.Empty;
            if (_paths.Count > sample.Length)
            {
                suffix += ", ...";
            }

            summary = $"Incremental build triggered for {_paths.Count} file(s){suffix}";
            _paths.Clear();
            return true;
        }
    }
}
