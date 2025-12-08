using System.Diagnostics;

namespace RobloxCS.Shared;

public sealed class RojoServerManager : IDisposable
{
    private IProcessHandle? _process;
    private string? _projectPath;

    public void Start(string projectFilePath, string workingDirectory, int port, bool verbose)
    {
        Stop();

        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            Logger.Warn("rojo serve skipped: unable to locate project file.");
            return;
        }

        var arguments = $"serve \"{projectFilePath}\" --port {port}";
        var startInfo = new ProcessStartInfo("rojo", arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };

        if (verbose)
        {
            Logger.Info($"Starting `rojo serve` on port {port}: {startInfo.FileName} {startInfo.Arguments}");
        }

        _projectPath = projectFilePath;
        _process = ProcessUtility.Start(startInfo);
        if (_process == null)
        {
            Logger.Warn("Failed to start `rojo serve`.");
        }
    }

    public void Stop()
    {
        if (_process == null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(true);
                if (!_process.WaitForExit(5000))
                {
                    Logger.Warn("Timed out waiting for `rojo serve` to exit.");
                }
            }

            if (_process.ExitCode != 0)
            {
                Logger.Warn($"`rojo serve` exited with code {_process.ExitCode} for project {_projectPath}.");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to stop `rojo serve`: {ex.Message}");
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _projectPath = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
