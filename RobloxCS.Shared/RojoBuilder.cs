using System.Diagnostics;

namespace RobloxCS.Shared;

public static class RojoBuilder
{
    public static bool RunBuild(string projectFilePath, string outputPath, string workingDirectory, bool verbose)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            Logger.Warn("rojo build skipped: unable to locate project file.");
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var arguments = $"build \"{projectFilePath}\" -o \"{outputPath}\"";
        var startInfo = new ProcessStartInfo("rojo", arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };

        if (verbose)
        {
            Logger.Info($"Executing: {startInfo.FileName} {startInfo.Arguments}");
        }

        try
        {
            var exitCode = ProcessUtility.Run(startInfo);
            if (exitCode != 0)
            {
                Logger.Warn($"rojo build exited with code {exitCode}.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to execute rojo build: {ex.Message}");
            return false;
        }
    }
}
