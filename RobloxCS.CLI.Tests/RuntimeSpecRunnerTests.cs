using System;
using System.IO;
using System.Runtime.InteropServices;
using RobloxCS;
using RobloxCS.CLI;
using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.CLI.Tests;

public class RuntimeSpecRunnerTests
{
    [Fact]
    public void Run_InvokesLuneExecutableWhenHarnessExists()
    {
        Logger.Exit = false;

        var tempRoot = Directory.CreateTempSubdirectory();
        try
        {
            var projectDir = tempRoot.FullName;
            var harnessDir = Path.Combine(projectDir, "tests", "runtime");
            var outputDir = Path.Combine(harnessDir, "out");
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(harnessDir, "run.lua"), "-- stub harness");

            var config = new ConfigData
            {
                SourceFolder = "src",
                OutputFolder = "tests/runtime/out",
                RojoProjectName = "TestProject",
                EntryPointArguments = Array.Empty<object>(),
                RojoServePort = ConfigData.DefaultRojoPort,
            };

            var result = new TranspileResult(
                projectDir,
                Path.Combine(projectDir, "src"),
                outputDir,
                null,
                config.RojoProjectName,
                config
            );

            var fakeToolsDir = Directory.CreateDirectory(Path.Combine(projectDir, "tools"));
            var logPath = Path.Combine(projectDir, "lune-invocation.log");
            Environment.SetEnvironmentVariable("ROBLOX_CS_LUNE_LOG", logPath);

            string fakeLunePath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                fakeLunePath = Path.Combine(fakeToolsDir.FullName, "lune.cmd");
                File.WriteAllText(fakeLunePath,
                    "@echo off\r\n"
                    + "echo %* > \"%ROBLOX_CS_LUNE_LOG%\"\r\n"
                    + "exit /b 0\r\n");
            }
            else
            {
                fakeLunePath = Path.Combine(fakeToolsDir.FullName, "lune");
                File.WriteAllText(fakeLunePath,
                    "#!/bin/sh\n"
                    + "printf \"%s\" \"$@\" > \"$ROBLOX_CS_LUNE_LOG\"\n");
                var chmod = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{fakeLunePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                chmod?.WaitForExit();
            }

            Environment.SetEnvironmentVariable("ROBLOX_CS_LUNE_PATH", fakeLunePath);

            RuntimeSpecRunner.Run(result, verbose: false);
            Assert.True(File.Exists(logPath), "Fake lune should log its invocation.");
            var args = File.ReadAllText(logPath).Replace('\\', '/');
            Assert.Contains("run", args);
            Assert.Contains("tests/runtime/run.lua", args);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROBLOX_CS_LUNE_LOG", null);
            Environment.SetEnvironmentVariable("ROBLOX_CS_LUNE_PATH", null);
            Logger.Exit = true;
            Directory.Delete(tempRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void Run_ReportsFailureWhenLuneFails()
    {
        Logger.Exit = false;

        var tempRoot = Directory.CreateTempSubdirectory();
        try
        {
            var projectDir = tempRoot.FullName;
            var harnessDir = Path.Combine(projectDir, "tests", "runtime");
            var outputDir = Path.Combine(harnessDir, "out");
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(harnessDir, "run.lua"), "-- stub harness");

            var config = new ConfigData
            {
                SourceFolder = "src",
                OutputFolder = "tests/runtime/out",
                RojoProjectName = "TestProject",
                EntryPointArguments = Array.Empty<object>(),
                RojoServePort = ConfigData.DefaultRojoPort,
            };

            var result = new TranspileResult(
                projectDir,
                Path.Combine(projectDir, "src"),
                outputDir,
                null,
                config.RojoProjectName,
                config
            );

            var fakeToolsDir = Directory.CreateDirectory(Path.Combine(projectDir, "tools"));
            var logPath = Path.Combine(projectDir, "lune-failure.log");
            Environment.SetEnvironmentVariable("ROBLOX_CS_LUNE_LOG", logPath);

            string fakeLunePath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                fakeLunePath = Path.Combine(fakeToolsDir.FullName, "lune.cmd");
                File.WriteAllText(fakeLunePath,
                    "@echo off\r\n"
                    + "echo failure > \"%ROBLOX_CS_LUNE_LOG%\"\r\n"
                    + "exit /b 1\r\n");
            }
            else
            {
                fakeLunePath = Path.Combine(fakeToolsDir.FullName, "lune");
                File.WriteAllText(fakeLunePath,
                    "#!/bin/sh\n"
                    + "echo failure > \"$ROBLOX_CS_LUNE_LOG\"\n"
                    + "exit 1\n");
                var chmod = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{fakeLunePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                chmod?.WaitForExit();
            }

            Environment.SetEnvironmentVariable("ROBLOX_CS_LUNE_PATH", fakeLunePath);

            var success = RuntimeSpecRunner.Run(result, verbose: false);

            Assert.False(success, "Runtime specs should surface failure from lune.");
            Assert.True(File.Exists(logPath), "Fake lune should log its failure.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROBLOX_CS_LUNE_LOG", null);
            Environment.SetEnvironmentVariable("ROBLOX_CS_LUNE_PATH", null);
            Logger.Exit = true;
            Directory.Delete(tempRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void Run_ReturnsFalseWhenHarnessMissing()
    {
        Logger.Exit = false;

        var tempRoot = Directory.CreateTempSubdirectory();
        try
        {
            var projectDir = tempRoot.FullName;
            var config = new ConfigData
            {
                SourceFolder = "src",
                OutputFolder = "tests/runtime/out",
                RojoProjectName = "TestProject",
                EntryPointArguments = Array.Empty<object>(),
                RojoServePort = ConfigData.DefaultRojoPort,
            };

            var result = new TranspileResult(
                projectDir,
                Path.Combine(projectDir, "src"),
                Path.Combine(projectDir, config.OutputFolder),
                null,
                config.RojoProjectName,
                config
            );

            var success = RuntimeSpecRunner.Run(result, verbose: true);

            Assert.False(success, "Runtime specs should be skipped when the harness is absent.");
        }
        finally
        {
            Logger.Exit = true;
            Directory.Delete(tempRoot.FullName, recursive: true);
        }
    }

    [Fact]
    public void Run_SkipsWhenLuneMissing()
    {
        Logger.Exit = false;

        var tempRoot = Directory.CreateTempSubdirectory();
        try
        {
            var projectDir = tempRoot.FullName;
            var harnessDir = Path.Combine(projectDir, "tests", "runtime");
            var outputDir = Path.Combine(harnessDir, "out");
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(harnessDir, "run.lua"), "-- stub harness");

            var config = new ConfigData
            {
                SourceFolder = "src",
                OutputFolder = "tests/runtime/out",
                RojoProjectName = "TestProject",
                EntryPointArguments = Array.Empty<object>(),
                RojoServePort = ConfigData.DefaultRojoPort,
            };

            var result = new TranspileResult(
                projectDir,
                Path.Combine(projectDir, "src"),
                outputDir,
                null,
                config.RojoProjectName,
                config
            );

            Environment.SetEnvironmentVariable("ROBLOX_CS_LUNE_PATH", Path.Combine(projectDir, "missing-lune"));

            var success = RuntimeSpecRunner.Run(result, verbose: true);

            Assert.False(success, "Runtime specs should skip when lune is not installed.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROBLOX_CS_LUNE_PATH", null);
            Logger.Exit = true;
            Directory.Delete(tempRoot.FullName, recursive: true);
        }
    }
}
