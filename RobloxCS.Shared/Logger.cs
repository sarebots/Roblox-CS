using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace RobloxCS.Shared;

public class CleanExitException : Exception
{
    public CleanExitException(string message)
        : base(message)
    {
        if (!Logger.Exit) return;

        Environment.Exit(1);
    }
}

public static class Logger
{
    private static readonly Regex DiagnosticCodeRegex = new(@"\[(ROBLOXCS\d+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex InlineDiagnosticCodeRegex = new(@"ROBLOXCS\d+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> ReportedDiagnosticHints = [];
    private static readonly Dictionary<string, int> DiagnosticCounts = new(StringComparer.OrdinalIgnoreCase);
    private const string IteratorGuardrailDocPath = "docs/ts-iter-guardrails.md";
    private static readonly Dictionary<string, string> DiagnosticHintOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ROBLOXCS3032"] = IteratorGuardrailDocPath,
        ["ROBLOXCS3033"] = IteratorGuardrailDocPath,
        ["ROBLOXCS3034"] = IteratorGuardrailDocPath,
        ["ROBLOXCS3035"] = IteratorGuardrailDocPath,
        ["ROBLOXCS3036"] = IteratorGuardrailDocPath,
        ["ROBLOXCS3042"] = "docs/struct-ref-out-guidance.md",
        ["ROBLOXCS3044"] = "docs/struct-ref-out-guidance.md",
    };
    private static bool _diagnosticSummaryPrinted;
    public static bool Exit { get; set; } = true;
    private const string _compilerError = " (roblox-cs compiler error)";

    public static void Ok(string message) => Log(message, ConsoleColor.Green, "OK");

    public static void Info(string message) => Log(message, ConsoleColor.Cyan, "INFO");

    public static CleanExitException Error(string message)
    {
        MaybeReportDiagnosticHint(message);
        PrintLoggedDiagnosticSummary();
        Log(message, ConsoleColor.Red, "ERROR");
        return new CleanExitException(message);
    }

    public static CleanExitException CompilerError(string message, SyntaxNode node) =>
        CodegenError(node, message + _compilerError);

    public static CleanExitException CompilerError(string message, SyntaxToken token) =>
        CodegenError(token, message + _compilerError);

    public static CleanExitException CompilerError(string message) => Error(message + _compilerError);

    public static CleanExitException CodegenError(SyntaxToken token, string message) =>
        Error($"{message}\n\t- {FormatLocation(token.GetLocation().GetLineSpan())}");

    public static void CodegenWarning(SyntaxToken token, string message)
    {
        var lineSpan = token.GetLocation().GetLineSpan();
        Warn($"{message}\n\t- {FormatLocation(lineSpan)}");
    }

    public static CleanExitException UnsupportedError(SyntaxNode node, string subject, bool useIs = false, bool useYet = true) =>
        UnsupportedError(node.GetFirstToken(), subject, useIs, useYet);

    public static CleanExitException UnsupportedError(SyntaxToken token, string subject, bool useIs = false, bool useYet = true) =>
        CodegenError(token, $"{subject} {(useIs ? "is" : "are")} not {(useYet ? "yet " : "")}supported, sorry!");

    public static CleanExitException CodegenError(SyntaxNode node, string message) =>
        CodegenError(node.GetFirstToken(), message);

    public static void CodegenWarning(SyntaxNode node, string message) =>
        CodegenWarning(node.GetFirstToken(), message);

    public static void HandleDiagnostic(Diagnostic diagnostic)
    {
        HashSet<string> ignoredCodes = ["CS7022", "CS0017" /* more than one entry point */];

        if (ignoredCodes.Contains(diagnostic.Id)) return;

        var lineSpan = diagnostic.Location.GetLineSpan();
        var diagnosticMessage = $"{diagnostic.Id}: {diagnostic.GetMessage()}";
        var location = $"\n\t- {FormatLocation(lineSpan)}";
        switch (diagnostic.Severity)
        {
            case DiagnosticSeverity.Error:
            {
                Error(diagnosticMessage + location);
                break;
            }
            case DiagnosticSeverity.Warning:
            {
                if (diagnostic.IsWarningAsError)
                    Error(diagnosticMessage + location);
                else
                    Warn(diagnosticMessage + location);

                break;
            }
            case DiagnosticSeverity.Info:
            {
                Info(diagnosticMessage);
                break;
            }
        }
    }

    public static void Warn(string message) => Log(message, ConsoleColor.Yellow, "WARN");

    public static void Debug(string message) => Log(message, ConsoleColor.Magenta, "DEBUG");

    private static void Log(string message, ConsoleColor color, string level)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine($"[{level}] {message}");
        Console.ForegroundColor = originalColor;
    }

    private static void MaybeReportDiagnosticHint(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var codes = ExtractDiagnosticCodes(message);
        if (codes.Count == 0)
        {
            return;
        }

        foreach (var code in codes)
        {
            if (ReportedDiagnosticHints.Add(code))
            {
                Info($"Hint: see docs/Diagnostics.md for guidance on {code}.");

                if (DiagnosticHintOverrides.TryGetValue(code, out var overrideDoc))
                {
                    Info($"Hint: see {overrideDoc} for guidance on {code}.");
                }
            }

            DiagnosticCounts.TryGetValue(code, out var count);
            DiagnosticCounts[code] = count + 1;
        }
    }

    private static void PrintLoggedDiagnosticSummary()
    {
        if (_diagnosticSummaryPrinted || DiagnosticCounts.Count == 0)
        {
            return;
        }

        _diagnosticSummaryPrinted = true;
        Info("Diagnostics summary:");
        foreach (var entry in DiagnosticCounts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            Info($"  {entry.Key}: {entry.Value} occurrence(s)");
        }

        Info("Refer to docs/Diagnostics.md for detailed guidance.");

        foreach (var doc in GetOverrideDocs(DiagnosticCounts.Keys))
        {
            Info($"See {doc} for additional guidance.");
        }
    }

    public static IEnumerable<string> GetOverrideDocs(IEnumerable<string>? codes)
    {
        if (codes is null)
        {
            return Array.Empty<string>();
        }

        var docs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var code in codes)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            var trimmed = code.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            var normalizedCodes = ExtractDiagnosticCodes(trimmed);
            foreach (var normalizedCode in normalizedCodes)
            {
                if (DiagnosticHintOverrides.TryGetValue(normalizedCode, out var doc) && !string.IsNullOrEmpty(doc))
                {
                    docs.Add(doc);
                }
            }
        }

        return docs.Count == 0
            ? Array.Empty<string>()
            : docs.OrderBy(doc => doc, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static HashSet<string> ExtractDiagnosticCodes(string message)
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(message))
        {
            return codes;
        }

        var bracketMatches = DiagnosticCodeRegex.Matches(message);
        foreach (Match match in bracketMatches)
        {
            if (match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                codes.Add(match.Groups[1].Value);
            }
        }

        var inlineMatches = InlineDiagnosticCodeRegex.Matches(message);
        foreach (Match match in inlineMatches)
        {
            if (!string.IsNullOrWhiteSpace(match.Value))
            {
                codes.Add(match.Value);
            }
        }

        return codes;
    }

    public static void ResetDiagnosticState()
    {
        ReportedDiagnosticHints.Clear();
        DiagnosticCounts.Clear();
        _diagnosticSummaryPrinted = false;
    }

    private static string FormatLocation(FileLinePositionSpan lineSpan) =>
        $"{(lineSpan.Path == "" ? "<anonymous>" : lineSpan.Path)}:{lineSpan.StartLinePosition.Line + 1}:{lineSpan.StartLinePosition.Character + 1}";
}
