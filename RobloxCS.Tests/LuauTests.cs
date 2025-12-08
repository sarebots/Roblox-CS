using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit.Abstractions;

namespace RobloxCS.Tests;

public class LuauTests(ITestOutputHelper testOutputHelper)
{
    private readonly string _cwd = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetExecutingAssembly()
                                                                                                                                   .Location))))!;
    
    [Theory]
    [InlineData("RuntimeLibTest")]
    public void LuauTests_Pass(string scriptName)
    {
        var lunePath = Path.GetFullPath("lune" + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : ""), _cwd);
        var runScriptArguments = $"run {scriptName}";
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = lunePath,
                Arguments = runScriptArguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _cwd
            }
        };

        try
        {
            try
            {
                process.Start();
            }
            catch (Win32Exception ex) when (ShouldSkipRuntimeRun(ex))
            {
                testOutputHelper.WriteLine($"Skipping Luau runtime test: {ex.Message}");
                return;
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            testOutputHelper.WriteLine($"{scriptName}.luau Errors:");
            testOutputHelper.WriteLine(error);
            Assert.True(string.IsNullOrWhiteSpace(error));
            testOutputHelper.WriteLine($"{scriptName}.luau Output:");
            testOutputHelper.WriteLine(output);
            Assert.True(string.IsNullOrWhiteSpace(output));
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            process.Dispose();
        }
    }

    private static bool ShouldSkipRuntimeRun(Win32Exception exception)
    {
        // Common native error codes: 2 (ENOENT), 8 (ENOEXEC), 13 (EACCES)
        return exception.NativeErrorCode is 2 or 8 or 13;
    }
}
