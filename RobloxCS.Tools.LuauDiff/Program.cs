using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

var legacyPath = string.Empty;
var v2Path = string.Empty;
string? summaryPath = null;

for (var i = 0; i < args.Length; i++)
{
    var arg = args[i];
    switch (arg)
    {
        case "--json":
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("Missing path after --json.");
                Environment.Exit(2);
            }

            summaryPath = args[i + 1];
            i += 1;
            break;

        case "-h":
        case "--help":
            PrintUsage();
            return;

        default:
            if (legacyPath.Length == 0)
            {
                legacyPath = arg;
            }
            else if (v2Path.Length == 0)
            {
                v2Path = arg;
            }
            else
            {
                Console.Error.WriteLine($"Unexpected argument: {arg}");
                Environment.Exit(2);
            }

            break;
    }
}

if (legacyPath.Length == 0 || v2Path.Length == 0)
{
    PrintUsage();
    Environment.Exit(2);
}

if (!File.Exists(legacyPath))
{
    Console.Error.WriteLine($"Legacy file not found: {legacyPath}");
    Environment.Exit(2);
}

if (!File.Exists(v2Path))
{
    Console.Error.WriteLine($"V2 file not found: {v2Path}");
    Environment.Exit(2);
}

var legacyLines = File.ReadAllLines(legacyPath);
var v2Lines = File.ReadAllLines(v2Path);

var max = Math.Max(legacyLines.Length, v2Lines.Length);
var differences = new List<LineDifference>();

for (var i = 0; i < max; i++)
{
    var legacyLine = i < legacyLines.Length ? legacyLines[i].TrimEnd() : "<eof>";
    var v2Line = i < v2Lines.Length ? v2Lines[i].TrimEnd() : "<eof>";

    if (!string.Equals(legacyLine, v2Line, StringComparison.Ordinal))
    {
        differences.Add(new LineDifference(i + 1, legacyLine, v2Line));
    }
}

var printLimit = 10;

if (differences.Count == 0)
{
    if (summaryPath is null)
    {
        Console.WriteLine("Files match.");
    }

    WriteSummary(summaryPath, legacyPath, v2Path, differences);
    return;
}

for (var i = 0; i < Math.Min(printLimit, differences.Count); i++)
{
    var diff = differences[i];
    Console.WriteLine($"Mismatch at line {diff.LineNumber}:");
    Console.WriteLine($"  legacy: {diff.LegacyLine}");
    Console.WriteLine($"  v2    : {diff.V2Line}");
}

if (differences.Count > printLimit)
{
    Console.WriteLine($"... {differences.Count - printLimit} additional difference(s) truncated.");
}

WriteSummary(summaryPath, legacyPath, v2Path, differences);
Environment.Exit(1);

static void WriteSummary(string? summaryPath, string legacyPath, string v2Path, IReadOnlyCollection<LineDifference> differences)
{
    if (summaryPath is null)
    {
        return;
    }

    var summary = new LuauDiffSummary
    {
        LegacyPath = Path.GetFullPath(legacyPath),
        V2Path = Path.GetFullPath(v2Path),
        Matches = differences.Count == 0,
        DifferenceCount = differences.Count,
        Differences = differences
            .Select(diff => new LuauDiffSummary.DiffEntry
            {
                LineNumber = diff.LineNumber,
                LegacyLine = diff.LegacyLine,
                V2Line = diff.V2Line,
            })
            .ToArray(),
    };

    var directory = Path.GetDirectoryName(summaryPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, options));
}

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run --project RobloxCS.Tools.LuauDiff [--json <summaryPath>] <legacy.lua> <transpiler.lua>");
}

internal sealed record LineDifference(int LineNumber, string LegacyLine, string V2Line);

internal sealed class LuauDiffSummary
{
    public string LegacyPath { get; init; } = string.Empty;
    public string V2Path { get; init; } = string.Empty;
    public bool Matches { get; init; }
    public int DifferenceCount { get; init; }
    public DiffEntry[] Differences { get; init; } = Array.Empty<DiffEntry>();

    internal sealed class DiffEntry
    {
        public int LineNumber { get; init; }
        public string LegacyLine { get; init; } = string.Empty;
        public string V2Line { get; init; } = string.Empty;
    }
}
