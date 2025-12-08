using System;
using System.Diagnostics;
using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.Tests;

public sealed class RojoBuilderTests : IDisposable
{
    private readonly Func<ProcessStartInfo, int> _originalRunner = ProcessUtility.Runner;

    [Fact]
    public void RunBuildInvokesRojoWithExpectedArguments()
    {
        var workingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingRoot);
        var projectPath = Path.Combine(workingRoot, "project.project.json");
        File.WriteAllText(projectPath, "{}");

        ProcessStartInfo? captured = null;
        ProcessUtility.Runner = info =>
        {
            captured = info;
            return 0;
        };

        var outputPath = Path.Combine(workingRoot, "dist", "game.rbxl");
        var success = RojoBuilder.RunBuild(projectPath, outputPath, workingRoot, verbose: true);

        Assert.True(success);
        Assert.NotNull(captured);
        Assert.Equal("rojo", captured!.FileName);
        Assert.Contains("build", captured.Arguments);
        Assert.Contains($"\"{projectPath}\"", captured.Arguments);
        Assert.Contains($"\"{outputPath}\"", captured.Arguments);
        Assert.Equal(workingRoot, captured.WorkingDirectory);
    }

    public void Dispose()
    {
        ProcessUtility.Runner = _originalRunner;
    }
}
