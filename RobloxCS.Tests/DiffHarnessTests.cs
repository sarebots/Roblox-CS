using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace RobloxCS.Tests;

public class DiffHarnessTests
{
    [Fact]
    public void LuauDiffReportsMatchForIdenticalFiles()
    {
        using var tempDir = new TempDirectory();
        var legacyPath = Path.Combine(tempDir.Path, "legacy.lua");
        var v2Path = Path.Combine(tempDir.Path, "v2.lua");

        const string content = "print(\"hello\")\n";
        File.WriteAllText(legacyPath, content);
        File.WriteAllText(v2Path, content);

        var projectRoot = GetProjectRoot();
        var toolProject = Path.Combine(projectRoot, "roblox-cs", "RobloxCS.Tools.LuauDiff", "RobloxCS.Tools.LuauDiff.csproj");

        var dotnetExe = ResolveDotnet();
        var psi = new ProcessStartInfo(dotnetExe)
        {
            Arguments = $"run --project \"{toolProject}\" \"{legacyPath}\" \"{v2Path}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = projectRoot,
        };
        psi.Environment["DOTNET"] = dotnetExe;
        if (!psi.Environment.ContainsKey("DOTNET_ROOT"))
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                psi.Environment["DOTNET_ROOT"] = Path.Combine(home, ".dotnet");
            }
        }

        if (!psi.Environment.ContainsKey("DOTNET_MULTILEVEL_LOOKUP"))
        {
            psi.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        }


        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet process");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(string.IsNullOrWhiteSpace(stderr), $"Expected no stderr but got: {stderr}");
        Assert.Contains("Files match.", stdout);
        Assert.Equal(0, process.ExitCode);
    }

    private static string GetProjectRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
    }

    private static string ResolveDotnet()
    {
        var candidate = Environment.GetEnvironmentVariable("DOTNET");
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            return candidate;
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            var dotnetFromHome = Path.Combine(home, ".dotnet", "dotnet");
            if (File.Exists(dotnetFromHome))
            {
                return dotnetFromHome;
            }
        }

        return "dotnet";
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
