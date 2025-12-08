using System;
using System.Collections.Generic;
using System.IO;
using RobloxCS.Shared;

namespace RobloxCS.CLI;

internal static class PluginWatchTargetBuilder
{
    internal static IReadOnlyList<WatchTarget> Build(ConfigData config, string projectDirectory)
    {
        if (config.Plugins.Count == 0)
        {
            return Array.Empty<WatchTarget>();
        }

        var results = new HashSet<WatchTarget>(WatchTargetComparer.Instance);

        foreach (var plugin in config.Plugins)
        {
            if (string.IsNullOrWhiteSpace(plugin.Assembly))
            {
                continue;
            }

            var assemblyPath = ResolvePath(projectDirectory, plugin.Assembly);
            if (assemblyPath == null)
            {
                continue;
            }

            var assemblyDirectory = Path.GetDirectoryName(assemblyPath);
            var assemblyFile = Path.GetFileName(assemblyPath);
            if (!string.IsNullOrEmpty(assemblyDirectory) && Directory.Exists(assemblyDirectory) && !string.IsNullOrEmpty(assemblyFile))
            {
                results.Add(new WatchTarget(assemblyDirectory, assemblyFile, false));
            }

            foreach (var watchEntry in plugin.Watch)
            {
                if (string.IsNullOrWhiteSpace(watchEntry))
                {
                    continue;
                }

                var watchPath = ResolvePath(projectDirectory, watchEntry);
                if (watchPath == null)
                {
                    continue;
                }

                if (Directory.Exists(watchPath))
                {
                    results.Add(new WatchTarget(watchPath, "*", true));
                }
                else if (File.Exists(watchPath))
                {
                    var dir = Path.GetDirectoryName(watchPath);
                    var file = Path.GetFileName(watchPath);
                    if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(file))
                    {
                        results.Add(new WatchTarget(dir, file, false));
                    }
                }
            }
        }

        return results.Count == 0 ? Array.Empty<WatchTarget>() : new List<WatchTarget>(results);
    }

    private static string? ResolvePath(string projectDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        return Path.IsPathRooted(relativePath)
            ? Path.GetFullPath(relativePath)
            : Path.GetFullPath(Path.Combine(projectDirectory, relativePath));
    }

    internal readonly record struct WatchTarget(string Directory, string Filter, bool IncludeSubdirectories);

    private sealed class WatchTargetComparer : IEqualityComparer<WatchTarget>
    {
        public static WatchTargetComparer Instance { get; } = new();

        public bool Equals(WatchTarget x, WatchTarget y) =>
            string.Equals(x.Directory, y.Directory, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Filter, y.Filter, StringComparison.OrdinalIgnoreCase)
            && x.IncludeSubdirectories == y.IncludeSubdirectories;

        public int GetHashCode(WatchTarget obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Directory, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Filter, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.IncludeSubdirectories);
            return hash.ToHashCode();
        }
    }
}
