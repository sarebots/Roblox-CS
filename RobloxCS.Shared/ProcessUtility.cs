using System.Diagnostics;

namespace RobloxCS.Shared;

public interface IProcessHandle : IDisposable
{
    bool HasExited { get; }
    int ExitCode { get; }
    void Kill(bool entireProcessTree);
    bool WaitForExit(int milliseconds);
}

public static class ProcessUtility
{
    private static Func<ProcessStartInfo, int> _runner = DefaultRunner;
    private static Func<ProcessStartInfo, IProcessHandle?> _starter = DefaultStarter;

    public static Func<ProcessStartInfo, int> Runner
    {
        get => _runner;
        set => _runner = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static Func<ProcessStartInfo, IProcessHandle?> Starter
    {
        get => _starter;
        set => _starter = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static int Run(ProcessStartInfo startInfo) => Runner(startInfo);

    public static IProcessHandle? Start(ProcessStartInfo startInfo) => Starter(startInfo);

    public static void Reset()
    {
        _runner = DefaultRunner;
        _starter = DefaultStarter;
    }

    private static int DefaultRunner(ProcessStartInfo startInfo)
    {
        using var handle = DefaultStarter(startInfo) ?? throw Logger.Error($"Failed to start process: {startInfo.FileName} {startInfo.Arguments}");
        handle.WaitForExit(-1);
        return handle.ExitCode;
    }

    private static IProcessHandle? DefaultStarter(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo);
        return process == null ? null : new ProcessHandle(process);
    }

    private sealed class ProcessHandle : IProcessHandle
    {
        private readonly Process _process;

        public ProcessHandle(Process process)
        {
            _process = process;
        }

        public bool HasExited => _process.HasExited;

        public int ExitCode => _process.HasExited ? _process.ExitCode : 0;

        public void Kill(bool entireProcessTree) => _process.Kill(entireProcessTree);

        public bool WaitForExit(int milliseconds) => _process.WaitForExit(milliseconds);

        public void Dispose() => _process.Dispose();
    }
}
