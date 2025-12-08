using System;
using System.IO;
using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.Tests;

public sealed class ConfigReaderTests
{
    [Fact]
    public void ReadAssignsDefaultRojoPortWhenMissing()
    {
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "roblox-cs.yml"),
                """
                SourceFolder: src
                OutputFolder: out
                RojoProjectName: default
                EntryPointArguments: []
                """);

            var config = ConfigReader.Read(directory);

            Assert.Equal(ConfigData.DefaultRojoPort, config.RojoServePort);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReadRespectsExplicitRojoPort()
    {
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "roblox-cs.yml"),
                """
                SourceFolder: src
                OutputFolder: out
                RojoProjectName: default
                EntryPointArguments: []
                RojoServePort: 55555
                """);

            var config = ConfigReader.Read(directory);

            Assert.Equal(55555, config.RojoServePort);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReadParsesPluginConfigurationAndProjectRoot()
    {
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "roblox-cs.yml"),
                """
                SourceFolder: src
                OutputFolder: out
                RojoProjectName: default
                EntryPointArguments: []
                RojoServePort: 34872
                Plugins:
                  - Assembly: plugins/MyPlugin/MyPlugin.dll
                    Type: Sample.Plugin
                    After: true
                    Watch:
                      - plugins/MyPlugin/src
                    Settings:
                      Mode: test
                """);

            var config = ConfigReader.Read(directory);

            Assert.Equal(directory, config.ProjectRoot);
            var plugin = Assert.Single(config.Plugins);
            Assert.Equal("plugins/MyPlugin/MyPlugin.dll", plugin.Assembly);
            Assert.Equal("Sample.Plugin", plugin.Type);
            Assert.True(plugin.After);
            Assert.Single(plugin.Watch, "plugins/MyPlugin/src");
            Assert.Equal("test", plugin.Settings["Mode"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
