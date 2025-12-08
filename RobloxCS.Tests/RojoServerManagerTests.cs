using System;
using System.Diagnostics;
using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.Tests;

public sealed class RojoServerManagerTests : IDisposable
{
    private readonly Func<ProcessStartInfo, IProcessHandle?> _originalStarter = ProcessUtility.Starter;

    [Fact]
    public void StartInvokesRojoServeWithPort()
    {
        var workingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingRoot);
        var projectPath = Path.Combine(workingRoot, "project.project.json");
        File.WriteAllText(projectPath, "{}");

        ProcessStartInfo? captured = null;
        var stub = new StubProcessHandle();
        ProcessUtility.Starter = info =>
        {
            captured = info;
            return stub;
        };

        using var manager = new RojoServerManager();
        manager.Start(projectPath, workingRoot, 55678, verbose: true);

        Assert.NotNull(captured);
        Assert.Equal("rojo", captured!.FileName);
        Assert.Contains("serve", captured.Arguments);
        Assert.Contains("55678", captured.Arguments);
        Assert.Equal(workingRoot, captured.WorkingDirectory);

        manager.Stop();
        Assert.True(stub.KillCalled);

        Directory.Delete(workingRoot, recursive: true);
    }

    [Fact]
    public void StartSkipsWhenProjectMissing()
    {
        var manager = new RojoServerManager();
        ProcessUtility.Starter = _ => throw new InvalidOperationException("Should not start process");

        manager.Start("missing.project.json", ".", 12345, verbose: false);
        manager.Stop();
    }

    public void Dispose()
    {
        ProcessUtility.Reset();
    }

    private sealed class StubProcessHandle : IProcessHandle
    {
        public bool KillCalled { get; private set; }

        public bool HasExited { get; private set; }

        public int ExitCode => 0;

        public void Dispose()
        {
        }

        public void Kill(bool entireProcessTree)
        {
            KillCalled = true;
            HasExited = true;
        }

        public bool WaitForExit(int milliseconds)
        {
            HasExited = true;
            return true;
        }
    }
}
