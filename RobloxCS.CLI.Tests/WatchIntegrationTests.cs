using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RobloxCS.CLI.Tests;

public sealed class WatchIntegrationTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task WatchMode_ReportsPluginFileChanges()
    {
        var repoRoot = RepoRoot;
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-watch-int-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            PrepareProject(tempDir, repoRoot);

            using var process = StartCliWatch(repoRoot, tempDir);
            var readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var mainSummaryTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pluginSummaryTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var outputLog = new ConcurrentQueue<string>();

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is null) return;
                outputLog.Enqueue(args.Data);

                if (!readyTcs.Task.IsCompleted && args.Data.Contains("Watching for changes", StringComparison.OrdinalIgnoreCase))
                {
                    readyTcs.TrySetResult();
                }

                if (args.Data.Contains("Incremental build triggered", StringComparison.OrdinalIgnoreCase))
                {
                    if (!mainSummaryTcs.Task.IsCompleted && args.Data.Contains("Main.cs", StringComparison.OrdinalIgnoreCase))
                    {
                        mainSummaryTcs.TrySetResult(args.Data);
                    }

                    if (!pluginSummaryTcs.Task.IsCompleted && args.Data.Contains("SampleTransformerPlugin.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        pluginSummaryTcs.TrySetResult(args.Data);
                    }
                }
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null) outputLog.Enqueue(args.Data);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await WaitWithTimeoutAsync(readyTcs.Task, "watch readiness");
            await Task.Delay(TimeSpan.FromMilliseconds(250));

            var sourcePath = Path.Combine(tempDir, "src", "Main.cs");
            File.AppendAllText(sourcePath, $"\n// touch {Guid.NewGuid():N}");

            string summary;
            try
            {
                summary = await WaitWithTimeoutAsync(mainSummaryTcs.Task, "source change summary");
            }
            catch (TimeoutException ex)
            {
                var combined = string.Join(Environment.NewLine, outputLog);
                throw new TimeoutException($"Timed out waiting for Main.cs summary. CLI output:{Environment.NewLine}{combined}", ex);
            }

            Assert.Contains("Main.cs", summary, StringComparison.OrdinalIgnoreCase);

            var pluginPath = Path.Combine(tempDir, "plugins", "SampleTransformerPlugin.dll");
            var artifactPath = Path.Combine(repoRoot, "tests", "artifacts", "SampleTransformerPlugin.dll");
            if (File.Exists(pluginPath))
            {
                File.Delete(pluginPath);
            }
            File.Copy(artifactPath, pluginPath, overwrite: true);

            string pluginSummary;
            try
            {
                pluginSummary = await WaitWithTimeoutAsync(pluginSummaryTcs.Task, "plugin change summary");
            }
            catch (TimeoutException ex)
            {
                var combined = string.Join(Environment.NewLine, outputLog);
                throw new TimeoutException($"Timed out waiting for plugin summary. CLI output:{Environment.NewLine}{combined}", ex);
            }

            Assert.Contains("SampleTransformerPlugin.dll", pluginSummary, StringComparison.OrdinalIgnoreCase);

            KillProcess(process);
            if (!process.HasExited)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cts.Token);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }

    private static Process StartCliWatch(string repoRoot, string projectDirectory)
    {
        var cliDll = LocateCliAssembly(repoRoot);

        var homeDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var dotnetCandidate = !string.IsNullOrEmpty(dotnetRoot)
            ? Path.Combine(dotnetRoot, "dotnet")
            : Path.Combine(homeDotnet, "dotnet");

        var psi = new ProcessStartInfo
        {
            FileName = File.Exists(dotnetCandidate) ? dotnetCandidate : "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repoRoot,
        };

        psi.ArgumentList.Add(cliDll);
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(projectDirectory);
        psi.ArgumentList.Add("--watch");
        psi.ArgumentList.Add("--no-serve");

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

        return Process.Start(psi) ?? throw new InvalidOperationException("Failed to start roblox-cs CLI.");
    }

    private static void PrepareProject(string projectDirectory, string repoRoot)
    {
        Directory.CreateDirectory(Path.Combine(projectDirectory, "src"));
        Directory.CreateDirectory(Path.Combine(projectDirectory, "plugins"));

        var config = """
SourceFolder: src
OutputFolder: out
RojoProjectName: default
RojoServePort: 34872
Plugins:
  - Assembly: plugins/SampleTransformerPlugin.dll
    Type: RobloxCS.SamplePlugins.SampleTransformerPlugin
    Settings:
      Label: integration-test
""";

        File.WriteAllText(Path.Combine(projectDirectory, "roblox-cs.yml"), config);

        var rojo = """
{
  "name": "default",
  "tree": {
    "$className": "DataModel",
    "ServerScriptService": {
      "$className": "ServerScriptService",
      "Scripts": {
        "$className": "Folder",
        "$path": "src"
      }
    }
  }
}
""";

        File.WriteAllText(Path.Combine(projectDirectory, "default.project.json"), rojo);

        var source = """
public static class Main
{
    public static void Run()
    {
        var value = 1;
    }
}
""";

        File.WriteAllText(Path.Combine(projectDirectory, "src", "Main.cs"), source);

        var artifactPath = Path.Combine(repoRoot, "tests", "artifacts", "SampleTransformerPlugin.dll");
        if (!File.Exists(artifactPath))
        {
            throw new FileNotFoundException("Sample transformer plugin artifact missing. Run dotnet build tests/Fixtures/SampleTransformerPlugin first.", artifactPath);
        }

        File.Copy(artifactPath, Path.Combine(projectDirectory, "plugins", "SampleTransformerPlugin.dll"), overwrite: true);
    }

    private static async Task WaitWithTimeoutAsync(Task task, string description)
    {
        using var cts = new CancellationTokenSource(DefaultTimeout);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
        if (completed != task)
        {
            throw new TimeoutException($"Timed out waiting for {description}.");
        }
        await task; // propagate exceptions
    }

    private static async Task<T> WaitWithTimeoutAsync<T>(Task<T> task, string description)
    {
        using var cts = new CancellationTokenSource(DefaultTimeout);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, cts.Token));
        if (completed != task)
        {
            throw new TimeoutException($"Timed out waiting for {description}.");
        }
        return await task;
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch
        {
            // best effort
        }
    }

    private static string LocateCliAssembly(string repoRoot)
    {
        var tfmDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configurationDirectory = tfmDirectory.Parent ?? throw new InvalidOperationException("Unable to determine configuration directory from test context.");

        var cliDll = Path.Combine(
            repoRoot,
            "roblox-cs",
            "RobloxCS.CLI",
            "bin",
            configurationDirectory.Name,
            tfmDirectory.Name,
            "RobloxCS.CLI.dll");

        if (!File.Exists(cliDll))
        {
            throw new FileNotFoundException($"CLI assembly not found at '{cliDll}'");
        }

        return cliDll;
    }

    private static string RepoRoot
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
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
}
