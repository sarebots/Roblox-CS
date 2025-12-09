using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RobloxCS.CLI.Templates;
using Xunit;

namespace RobloxCS.CLI.Tests;

public class CliProcessTests
{
    private static string RepoRoot
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);

            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Unable to locate repository root from test context.");
        }
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunCliAsync(params string[] arguments)
    {
        var homeDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var dotnetCandidate = !string.IsNullOrEmpty(dotnetRoot)
            ? Path.Combine(dotnetRoot, "dotnet")
            : Path.Combine(homeDotnet, "dotnet");

        var tfmDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configurationDirectory = tfmDirectory.Parent ?? throw new InvalidOperationException("Unable to determine configuration directory from test context.");
        var cliDll = Path.Combine(
            RepoRoot,
            "RobloxCS.CLI",
            "bin",
            configurationDirectory.Name,
            tfmDirectory.Name,
            "RobloxCS.CLI.dll");

        if (!File.Exists(cliDll))
        {
            throw new FileNotFoundException($"CLI assembly not found at '{cliDll}'");
        }

        var psi = new ProcessStartInfo
        {
            FileName = File.Exists(dotnetCandidate) ? dotnetCandidate : "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot,
        };

        psi.ArgumentList.Add(cliDll);

        foreach (var argument in arguments)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                psi.ArgumentList.Add(argument);
            }
        }

        var dotnetRootDir = File.Exists(dotnetCandidate)
            ? Path.GetDirectoryName(dotnetCandidate)
            : (!string.IsNullOrEmpty(dotnetRoot) ? dotnetRoot : homeDotnet);
        
        if (!string.IsNullOrEmpty(dotnetRootDir) && Directory.Exists(dotnetRootDir))
        {
            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            psi.EnvironmentVariables["DOTNET_ROOT"] = dotnetRootDir;
            psi.EnvironmentVariables["PATH"] = $"{dotnetRootDir}{Path.PathSeparator}{existingPath}";
            psi.EnvironmentVariables["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        }

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(process.WaitForExitAsync(), stdoutTask, stderrTask);

        var rawStdout = stdoutTask.Result.Trim();
        var rawStderr = stderrTask.Result.Trim();

        if (string.IsNullOrWhiteSpace(rawStdout) && !string.IsNullOrWhiteSpace(rawStderr))
        {
            rawStdout = rawStderr;
            rawStderr = string.Empty;
        }

        return (process.ExitCode, rawStdout, rawStderr);
    }

    [Fact]
    public async Task VersionCommand_PrintsSemver()
    {
        var (code, stdout, stderr) = await RunCliAsync("--version");
        Assert.True(code == 0, $"Exit {code}. StdOut: {stdout}\nStdErr: {stderr}");
        Assert.False(string.IsNullOrWhiteSpace(stdout), $"Expected version string, but stdout was '{stdout}' and stderr was '{stderr}'.");

        var versionLine = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? string.Empty;

        var match = Regex.Match(versionLine, @"\d+\.\d+\.\d+");
        Assert.True(match.Success, $"Expected version in stdout, but saw '{stdout}'.");
        Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
    }

    [Fact]
    public async Task SingleFileCommand_PrintsLuauOutput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "Sample.cs");
        await File.WriteAllTextAsync(sourcePath, "public static class Sample { public static void Main() { int value = 0; } }");

        var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

        try
        {
            Assert.True(code == 0, $"Exit {code}. StdOut: {stdout}\nStdErr: {stderr}");
            Assert.Contains("local Sample", stdout);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task HelpCommand_ReferencesIteratorGuardrails()
    {
        var (code, stdout, stderr) = await RunCliAsync("--help");

        Assert.Equal(0, code);
        Assert.Contains("--macro-iterator-helpers", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docs/ts-iter-guardrails.md", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
    }

    [Fact]
    public async Task SingleFileCommand_WithDiagnostics_ExitsNonZero()
    {
        var (tempDir, sourcePath) = await CreatePromiseTelemetrySourceAsync();
        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);
            Assert.NotEqual(0, code);
            Assert.Contains("[ROBLOXCS3017]", stdout, StringComparison.Ordinal);
            Assert.Contains("docs/Diagnostics.md", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Diagnostics summary:", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3017", stdout, StringComparison.OrdinalIgnoreCase);
            var summaryStart = stdout.IndexOf("Diagnostics summary:", StringComparison.OrdinalIgnoreCase);
            var summaryEnd = stdout.IndexOf("Refer to docs/Diagnostics.md", summaryStart, StringComparison.OrdinalIgnoreCase);
            if (summaryStart >= 0 && summaryEnd > summaryStart)
            {
                var summaryBlock = stdout.Substring(summaryStart, summaryEnd - summaryStart);
                Assert.DoesNotContain("\n  CS", summaryBlock, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_DiagnosticSummaryResetsBetweenRuns()
    {
        var (tempDir, sourcePath) = await CreatePromiseTelemetrySourceAsync();
        try
        {
            var (firstCode, firstStdout, _) = await RunCliAsync("--single-file", sourcePath);
            var (secondCode, secondStdout, _) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, firstCode);
            Assert.NotEqual(0, secondCode);
            Assert.Contains("ROBLOXCS3017: 1 occurrence(s)", firstStdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3017: 1 occurrence(s)", secondStdout, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_DisabledIteratorHelpers_EmitsDiagnostic()
    {
        var (tempDir, sourcePath) = await CreateIteratorHelperSourceAsync();
        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--macro-iterator-helpers=false", "--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("[ROBLOXCS3032]", stdout, StringComparison.Ordinal);
            Assert.Contains("docs/ts-iter-guardrails.md", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_RecordDiagnosticSurfaces()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "Record.cs");
        await File.WriteAllTextAsync(sourcePath, "public record Person(string Name);");

        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("[ROBLOXCS3041]", stdout, StringComparison.Ordinal);
            Assert.Contains("Record declarations are not supported yet", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_RefStructDiagnosticSurfaces()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "RefStruct.cs");
        await File.WriteAllTextAsync(sourcePath, "public ref struct Buffer { public System.Span<int> Data; }");

        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("[ROBLOXCS3042]", stdout, StringComparison.Ordinal);
            Assert.Contains("ref struct declarations are not supported yet", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_RefLikeFieldDiagnosticSurfaces()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "RefField.cs");
        await File.WriteAllTextAsync(sourcePath, """
            public struct Container
            {
                public System.Span<int> Buffer;
            }
            """);

        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.True(
                stdout.Contains("[ROBLOXCS3044]", StringComparison.Ordinal) ||
                stdout.Contains("CS8345", StringComparison.OrdinalIgnoreCase),
                $"Expected ROBLOXCS3044 or CS8345 but saw: {stdout}");
            if (stdout.Contains("[ROBLOXCS3044]", StringComparison.Ordinal))
            {
                Assert.Contains("fields, properties, or indexers of ref-like types", stdout, StringComparison.OrdinalIgnoreCase);
            }
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_StructMethodDiagnosticSurfaces()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "StructMethod.cs");
        await File.WriteAllTextAsync(sourcePath, """
            public struct Worker
            {
                public void Run() { }
            }
            """);

        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("[ROBLOXCS3043]", stdout, StringComparison.Ordinal);
            Assert.Contains("Struct methods are not supported yet", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_RefLikePropertyDiagnosticSurfaces()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "RefProperty.cs");
        await File.WriteAllTextAsync(sourcePath, """
            public struct Holder
            {
                public System.Span<int> Data { get; set; }
            }
            """);

        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.True(
                stdout.Contains("[ROBLOXCS3044]", StringComparison.Ordinal) ||
                stdout.Contains("CS8345", StringComparison.OrdinalIgnoreCase),
                $"Expected ROBLOXCS3044 or CS8345 but saw: {stdout}");
            if (stdout.Contains("[ROBLOXCS3044]", StringComparison.Ordinal))
            {
                Assert.Contains("fields, properties, or indexers of ref-like types", stdout, StringComparison.OrdinalIgnoreCase);
            }
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_RefLikeIndexerDiagnosticSurfaces()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "RefIndexer.cs");
        await File.WriteAllTextAsync(sourcePath, """
            public struct Buffer
            {
                public System.Span<int> this[int index]
                {
                    get => System.Span<int>.Empty;
                    set { }
                }
            }
            """);

        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.True(
                stdout.Contains("[ROBLOXCS3044]", StringComparison.Ordinal) ||
                stdout.Contains("CS8345", StringComparison.OrdinalIgnoreCase),
                $"Expected ROBLOXCS3044 or CS8345 but saw: {stdout}");
            if (stdout.Contains("[ROBLOXCS3044]", StringComparison.Ordinal))
            {
                Assert.Contains("fields, properties, or indexers of ref-like types", stdout, StringComparison.OrdinalIgnoreCase);
            }
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_RefLikeDiagnosticSummaryIncludesCode()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "RefSummary.cs");
        await File.WriteAllTextAsync(sourcePath, """
            public struct Container
            {
                public System.Span<int> Buffer;
            }
            """);

        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("Diagnostics summary:", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3044", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("docs/Diagnostics.md", stdout, StringComparison.OrdinalIgnoreCase);
            if (stdout.Contains("ROBLOXCS3044", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("docs/struct-ref-out-guidance.md", stdout, StringComparison.OrdinalIgnoreCase);
            }
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_RecordDiagnosticSummaryIncludesCode()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "Record.cs");
        await File.WriteAllTextAsync(sourcePath, "public record Person(string Name);");

        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("Diagnostics summary:", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3041", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("docs/Diagnostics.md", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_RefStructDiagnosticSummaryIncludesCode()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "RefStruct.cs");
        await File.WriteAllTextAsync(sourcePath, "public ref struct Buffer { public System.Span<int> Data; }");

        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("Diagnostics summary:", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3042", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("docs/Diagnostics.md", stdout, StringComparison.OrdinalIgnoreCase);
            if (stdout.Contains("ROBLOXCS3042", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("docs/struct-ref-out-guidance.md", stdout, StringComparison.OrdinalIgnoreCase);
            }
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_StructMethodDiagnosticSummaryIncludesCode()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "StructMethodSummary.cs");
        await File.WriteAllTextAsync(sourcePath, "public struct Worker { public void Run() { } }");

        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("Diagnostics summary:", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3043", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("docs/Diagnostics.md", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_DisabledIteratorHelpers_ShowsHintAndSummary()
    {
        var (tempDir, sourcePath) = await CreateIteratorHelperSourceAsync();
        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--macro-iterator-helpers=false", "--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("Diagnostics summary:", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3032", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3032: 1 occurrence", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("docs/ts-iter-guardrails.md", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("docs/Diagnostics.md", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_IteratorHelper_InvalidArguments_IncludesSummary()
    {
        var (tempDir, sourcePath) = await CreateIteratorHelperArgumentCountSourceAsync();
        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("[ROBLOXCS3033]", stdout, StringComparison.Ordinal);
            Assert.Contains("Diagnostics summary:", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3033: 1 occurrence", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("docs/ts-iter-guardrails.md", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_IteratorHelper_InvalidSource_IncludesSummary()
    {
        var (tempDir, sourcePath) = await CreateIteratorHelperSourceTypeSourceAsync();
        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("[ROBLOXCS3034]", stdout, StringComparison.Ordinal);
            Assert.Contains("Diagnostics summary:", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3034: 1 occurrence", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("docs/ts-iter-guardrails.md", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_ArrayFlatten_InvalidArguments_IncludesSummary()
    {
        var (tempDir, sourcePath) = await CreateArrayFlattenArgumentCountSourceAsync();
        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("[ROBLOXCS3035]", stdout, StringComparison.Ordinal);
            Assert.Contains("Diagnostics summary:", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3035: 1 occurrence", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("docs/ts-iter-guardrails.md", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_ArrayFlatten_InvalidSource_IncludesSummary()
    {
        var (tempDir, sourcePath) = await CreateArrayFlattenSourceTypeSourceAsync();
        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("[ROBLOXCS3036]", stdout, StringComparison.Ordinal);
            Assert.Contains("Diagnostics summary:", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3036: 1 occurrence", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("docs/ts-iter-guardrails.md", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_AwaitForeach_EmitsDiagnostic()
    {
        var (tempDir, sourcePath) = await CreateAwaitForeachSourceAsync();
        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("[ROBLOXCS3019]", stdout, StringComparison.Ordinal);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_AwaitForeach_IncludesSummary()
    {
        var (tempDir, sourcePath) = await CreateAwaitForeachSourceAsync();
        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("[ROBLOXCS3019]", stdout, StringComparison.Ordinal);
            Assert.Contains("Diagnostics summary:", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3019", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_AsyncIteratorYield_EmitsDiagnostic()
    {
        var (tempDir, sourcePath) = await CreateAsyncIteratorSourceAsync();
        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("[ROBLOXCS3020]", stdout, StringComparison.Ordinal);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_AsyncIteratorYield_IncludesSummary()
    {
        var (tempDir, sourcePath) = await CreateAsyncIteratorSourceAsync();
        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.NotEqual(0, code);
            Assert.Contains("[ROBLOXCS3020]", stdout, StringComparison.Ordinal);
            Assert.Contains("Diagnostics summary:", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3020", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_StaticInterfaceMember_Succeeds()
    {
        var (tempDir, sourcePath) = await CreateStaticInterfaceSourceAsync();
        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.Equal(0, code);
            Assert.Contains("type IStaticy", stdout, StringComparison.Ordinal);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_StaticInterfaceMember_DoesNotEmitDiagnostics()
    {
        var (tempDir, sourcePath) = await CreateStaticInterfaceSourceAsync();
        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.Equal(0, code);
            Assert.DoesNotContain("Diagnostics summary:", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ROBLOXCS3045", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SingleFileCommand_StructIndexer_Succeeds()
    {
        var (tempDir, sourcePath) = await CreateStructIndexerSourceAsync();
        try
        {
            var (code, stdout, stderr) = await RunCliAsync("--single-file", sourcePath);

            Assert.Equal(0, code);
            Assert.Contains("type Buffer = {", stdout, StringComparison.Ordinal);
            Assert.Contains("[number]: number", stdout, StringComparison.Ordinal);
            Assert.True(string.IsNullOrEmpty(stderr), $"Expected empty stderr but saw: {stderr}");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task NewCommand_DryRun_CompletesSuccessfully()
    {
        var (code, stdout, stderr) = await RunCliAsync("new", "unity-roll-a-ball", "--dry-run", "--name", "CLI Game", "--verify");
        Assert.True(code == 0, $"Exit {code}. StdOut: {stdout}\nStdErr: {stderr}");
        Assert.Contains("dry-run", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unity-roll-a-ball", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Verification skipped", stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Project namespace", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NewCommand_VerifyRojo_FailureProducesErrorAndSummary()
    {
        var original = Environment.GetEnvironmentVariable("ROBLOX_CS_FORCE_ROJO_RESULT");
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        Environment.SetEnvironmentVariable("ROBLOX_CS_FORCE_ROJO_RESULT", "fail");

        try
        {
            var (code, stdout, stderr) = await RunCliAsync("new", "unity-roll-a-ball", "--directory", tempDir, "--verify-rojo");
            Assert.NotEqual(0, code);
            var summaryPath = Path.Combine(tempDir, TemplateVerifier.SummaryFileName);
            Assert.True(File.Exists(summaryPath), $"Expected verification summary at {summaryPath}");

            using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
            Assert.Equal("unity-roll-a-ball", summary.RootElement.GetProperty("templateId").GetString());
            Assert.False(summary.RootElement.GetProperty("succeeded").GetBoolean());
            Assert.True(summary.RootElement.GetProperty("durationMs").GetDouble() >= 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROBLOX_CS_FORCE_ROJO_RESULT", original);
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static async Task<(string TempDirectory, string SourcePath)> CreatePromiseTelemetrySourceAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "Sample.cs");
        await File.WriteAllTextAsync(sourcePath,
            """
            using System;
            using Roblox;

            namespace Roblox
            {
                public class Promise
                {
                    public static Promise Resolve(int value) => new Promise();
                    public static Promise Timeout(Promise promise, double seconds, object? reason = null) => new Promise();
                    public Promise Catch(Func<object?, Promise> handler) => this;
                }
            }

            public class Sample
            {
                public void Run()
                {
                    Roblox.Promise.Timeout(Roblox.Promise.Resolve(0), 1);
                }
            }
            """);

        return (tempDir, sourcePath);
    }

    private static async Task<(string TempDirectory, string SourcePath)> CreateIteratorHelperSourceAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "IteratorSample.cs");
        await File.WriteAllTextAsync(sourcePath,
            """
            using Roblox;

            public static class IteratorSample
            {
                public static void Run()
                {
                    foreach (var value in TS.iter(new[] { 1, 2 }))
                    {
                        _ = value;
                    }
                }
            }
            """);

        return (tempDir, sourcePath);
    }

    private static async Task<(string TempDirectory, string SourcePath)> CreateIteratorHelperArgumentCountSourceAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "IteratorArgumentCount.cs");
        await File.WriteAllTextAsync(sourcePath,
            """
            using Roblox;

            public static class IteratorArgumentCount
            {
                public static void Run()
                {
                    foreach (var value in TS.iter(new[] { 1, 2 }, new[] { 3 }))
                    {
                        _ = value;
                    }
                }
            }
            """);

        return (tempDir, sourcePath);
    }

    private static async Task<(string TempDirectory, string SourcePath)> CreateIteratorHelperSourceTypeSourceAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "IteratorSourceType.cs");
        await File.WriteAllTextAsync(sourcePath,
            """
            using Roblox;

            public static class IteratorSourceType
            {
                public static void Run()
                {
                    foreach (var value in TS.iter(123))
                    {
                        _ = value;
                    }
                }
            }
            """);

        return (tempDir, sourcePath);
    }

    private static async Task<(string TempDirectory, string SourcePath)> CreateArrayFlattenArgumentCountSourceAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "ArrayFlattenArgumentCount.cs");
        await File.WriteAllTextAsync(sourcePath,
            """
            using Roblox;

            public static class ArrayFlattenArgumentCount
            {
                public static void Run()
                {
                    foreach (var value in TS.array_flatten(new[] { new[] { 1 } }, new[] { new[] { 2 } }))
                    {
                        _ = value;
                    }
                }
            }
            """);

        return (tempDir, sourcePath);
    }

    private static async Task<(string TempDirectory, string SourcePath)> CreateArrayFlattenSourceTypeSourceAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "ArrayFlattenSourceType.cs");
        await File.WriteAllTextAsync(sourcePath,
            """
            using Roblox;

            public static class ArrayFlattenSourceType
            {
                public static void Run()
                {
                    foreach (var value in TS.array_flatten(new[] { 1, 2, 3 }))
                    {
                        _ = value;
                    }
                }
            }
            """);

        return (tempDir, sourcePath);
    }

    private static async Task<(string TempDirectory, string SourcePath)> CreateAwaitForeachSourceAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "AwaitForeachSample.cs");
        await File.WriteAllTextAsync(sourcePath,
            """
            using System.Collections.Generic;
            using System.Threading.Tasks;

            public static class AwaitForeachSample
            {
                public static async Task Run(IAsyncEnumerable<int> values)
                {
                    await foreach (var value in values)
                    {
                        _ = value;
                    }
                }
            }
            """);

        return (tempDir, sourcePath);
    }

    private static async Task<(string TempDirectory, string SourcePath)> CreateAsyncIteratorSourceAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "AsyncIteratorSample.cs");
        await File.WriteAllTextAsync(sourcePath,
            """
            using System.Collections.Generic;
            using System.Threading.Tasks;

            public static class AsyncIteratorSample
            {
                public static async IAsyncEnumerable<int> Generate()
                {
                    yield return 1;
                    await Task.Yield();
                }

                public static async Task Run()
                {
                    await foreach (var value in Generate())
                    {
                        _ = value;
                    }
                }
            }
            """);

        return (tempDir, sourcePath);
    }

    private static async Task<(string TempDirectory, string SourcePath)> CreateStaticInterfaceSourceAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "StaticInterfaceSample.cs");
        await File.WriteAllTextAsync(sourcePath,
            """
            public interface IStaticy
            {
                static int Count => 0;
            }
            """);

        return (tempDir, sourcePath);
    }

    private static async Task<(string TempDirectory, string SourcePath)> CreateStructIndexerSourceAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-cli-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, "StructIndexerSample.cs");
        await File.WriteAllTextAsync(sourcePath,
            """
            struct Buffer
            {
                public int this[int index]
                {
                    get { return index; }
                    set { _ = value; }
                }

                public int Value;
            }
            """);

        return (tempDir, sourcePath);
    }

}
