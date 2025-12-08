using System;
using System.Collections.Generic;
using System.IO;
using RobloxCS.Shared;
using RobloxCS.Transformers.Plugins;
using RobloxCS.SamplePlugins;
using Xunit;

namespace RobloxCS.Tests.TransformerTests;

public class TransformerPluginLoaderWarningTests
{
    [Fact]
    public void LoadWarnsWhenAssemblyMissing()
    {
        var config = new ConfigData
        {
            SourceFolder = "src",
            OutputFolder = "out",
            EntryPointArguments = Array.Empty<object>(),
            RojoProjectName = "default",
            RojoServePort = ConfigData.DefaultRojoPort,
            Plugins =
            [
                new TransformerPluginConfig
                {
                    Assembly = "missing/Plugin.dll"
                }
            ]
        };

        using var writer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(writer);

        try
        {
            var result = TransformerPluginLoader.Load(config, Directory.GetCurrentDirectory());
            Assert.Empty(result);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Contains("assembly not found", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadWarnsWhenTypeMissing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var sourceAssemblyPath = typeof(SampleTransformerPlugin).Assembly.Location
            ?? throw new InvalidOperationException("Unable to resolve sample plugin assembly path.");
        var assemblyPath = Path.Combine(tempDir, Path.GetFileName(sourceAssemblyPath));
        File.Copy(sourceAssemblyPath, assemblyPath, overwrite: true);

        try
        {
            var config = new ConfigData
            {
                SourceFolder = "src",
                OutputFolder = "out",
                EntryPointArguments = Array.Empty<object>(),
                RojoProjectName = "default",
                RojoServePort = ConfigData.DefaultRojoPort,
                Plugins =
                [
                    new TransformerPluginConfig
                    {
                        Assembly = Path.GetFileName(assemblyPath),
                        Type = "Missing.Plugin"
                    }
                ]
            };

            using var writer = new StringWriter();
            var original = Console.Out;
            Console.SetOut(writer);

            try
            {
                var result = TransformerPluginLoader.Load(config, tempDir);
                Assert.Empty(result);
            }
            finally
            {
                Console.SetOut(original);
            }

            Assert.Contains("was not found", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

}
