using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using RobloxCS;
using RobloxCS.Shared;

namespace RobloxCS.CLI;

internal static class RuntimeSpecRunner
{
    private const string LunePathEnvVar = "ROBLOX_CS_LUNE_PATH";

    internal static bool Run(TranspileResult result, bool verbose)
    {
        var harnessPath = Path.Combine(result.ProjectDirectory, "tests", "runtime", "run.lua");
        if (!File.Exists(harnessPath))
        {
            harnessPath = Path.Combine(result.ProjectDirectory, "roblox-cs", "tests", "runtime", "run.lua");
            if (!File.Exists(harnessPath))
            {
                if (verbose)
                {
                    Logger.Warn("Runtime harness not found (tests/runtime/run.lua); skipping runtime specs.");
                }

                return false;
            }
        }

        var luneExecutable = Environment.GetEnvironmentVariable(LunePathEnvVar);
        if (string.IsNullOrWhiteSpace(luneExecutable))
        {
            luneExecutable = "lune";
        }

        if (verbose)
        {
            Logger.Info("Running runtime specs via Lune.");
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = luneExecutable,
                Arguments = $"run \"{harnessPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = result.ProjectDirectory,
            }
        };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 8 or 13)
        {
            Logger.Warn($"Unable to launch `lune` (runtime specs skipped): {ex.Message}");
            return false;
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrWhiteSpace(error) || process.ExitCode != 0)
        {
            Logger.Error($"Runtime specs failed:\n{error}{(string.IsNullOrWhiteSpace(output) ? string.Empty : $"\n{output}")}");
            return false;
        }

        if (verbose && !string.IsNullOrWhiteSpace(output))
        {
            Console.Write(output);
        }
        else if (!verbose)
        {
            Logger.Ok("Runtime specs passed.");
        }

        return true;
    }
}
