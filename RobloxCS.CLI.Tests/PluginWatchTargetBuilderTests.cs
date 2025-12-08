using System;
using System.Collections.Generic;
using System.IO;
using RobloxCS.CLI;
using RobloxCS.SamplePlugins;
using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.CLI.Tests;

public class PluginWatchTargetBuilderTests
{
    [Fact]
    public void BuildReturnsAssemblyAndWatchTargets()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-watch-{Guid.NewGuid():N}");
        var pluginDir = Path.Combine(tempDir, "plugins", "Example");
        var pluginAssembly = Path.Combine(pluginDir, "Example.dll");
        var watchDir = Path.Combine(pluginDir, "src");

        Directory.CreateDirectory(pluginDir);
        Directory.CreateDirectory(watchDir);
        using (File.Create(pluginAssembly)) { }

        try
        {
            var config = new ConfigData
            {
                SourceFolder = "src",
                OutputFolder = "out",
                EntryPointArguments = Array.Empty<object>(),
                RojoProjectName = "default",
                RojoServePort = ConfigData.DefaultRojoPort,
                ProjectRoot = tempDir,
                Plugins =
                [
                    new TransformerPluginConfig
                    {
                        Assembly = Path.GetRelativePath(tempDir, pluginAssembly),
                        Watch = new List<string> { Path.GetRelativePath(tempDir, watchDir) }
                    }
                ]
            };

            var targets = PluginWatchTargetBuilder.Build(config, tempDir);

            Assert.Contains(targets, target =>
                string.Equals(target.Directory, pluginDir, StringComparison.OrdinalIgnoreCase)
                && string.Equals(target.Filter, Path.GetFileName(pluginAssembly), StringComparison.OrdinalIgnoreCase)
                && !target.IncludeSubdirectories);

            Assert.Contains(targets, target =>
                string.Equals(target.Directory, watchDir, StringComparison.OrdinalIgnoreCase)
                && target.IncludeSubdirectories);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void BuildHandlesAbsolutePluginAssembly()
    {
        var pluginAssembly = typeof(RobloxCS.SamplePlugins.SampleTransformerPlugin).Assembly.Location
            ?? throw new InvalidOperationException("Unable to resolve sample plugin assembly path.");
        var projectRoot = Path.GetDirectoryName(pluginAssembly) ?? throw new InvalidOperationException();

        var config = new ConfigData
        {
            SourceFolder = "src",
            OutputFolder = "out",
            EntryPointArguments = Array.Empty<object>(),
            RojoProjectName = "default",
            RojoServePort = ConfigData.DefaultRojoPort,
            ProjectRoot = projectRoot,
            Plugins =
            [
                new TransformerPluginConfig
                {
                    Assembly = pluginAssembly,
                }
            ]
        };

        var targets = PluginWatchTargetBuilder.Build(config, projectRoot);

        Assert.Contains(targets, target =>
            string.Equals(target.Directory, projectRoot, StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.Filter, Path.GetFileName(pluginAssembly), StringComparison.OrdinalIgnoreCase));
    }
}
