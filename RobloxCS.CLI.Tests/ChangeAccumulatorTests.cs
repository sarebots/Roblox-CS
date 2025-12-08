using System.IO;
using RobloxCS.CLI;
using Xunit;

namespace RobloxCS.CLI.Tests;

public class ChangeAccumulatorTests
{
    [Fact]
    public void TryTakeSummary_ReturnsFalseWhenEmpty()
    {
        var accumulator = new ChangeAccumulator();

        var result = accumulator.TryTakeSummary(out var summary);

        Assert.False(result);
        Assert.Equal(string.Empty, summary);
    }

    [Fact]
    public void TryTakeSummary_ListsPluginFiles()
    {
        var accumulator = new ChangeAccumulator();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var pluginFile = Path.Combine(tempDir, "Plugin.dll");
        File.WriteAllText(pluginFile, string.Empty);
        var sourceFile = Path.Combine(tempDir, "Sample.cs");
        File.WriteAllText(sourceFile, "public class Sample {}");

        try
        {
            accumulator.Add(pluginFile);
            accumulator.Add(sourceFile);

            Assert.True(accumulator.TryTakeSummary(out var summary));
            Assert.Contains("Plugin.dll", summary);
            Assert.Contains("Sample.cs", summary);
            Assert.Contains("2 file(s)", summary);

            // second call should be empty after flush
            Assert.False(accumulator.TryTakeSummary(out _));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
