using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.Luau;
using RobloxCS.Shared;
using RobloxCS.Transformers.Plugins;
using RobloxCS.SamplePlugins;
using Xunit;

namespace RobloxCS.Tests.TransformerTests;

public class TransformerPluginLoaderTests
{
    [Fact]
    public void TransformerPlugin_IsAppliedBeforeBuiltInTransforms()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"robloxcs-plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var sourceAssemblyPath = typeof(SampleTransformerPlugin).Assembly.Location
                ?? throw new InvalidOperationException("Unable to resolve sample plugin assembly path.");
            var pluginPath = Path.Combine(tempDir, Path.GetFileName(sourceAssemblyPath));
            File.Copy(sourceAssemblyPath, pluginPath, overwrite: true);
            var relativePluginPath = Path.GetRelativePath(tempDir, pluginPath);

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
                        Assembly = relativePluginPath,
                        Type = typeof(SampleTransformerPlugin).FullName,
                        After = false,
                        Settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Label"] = "integration"
                        }
                    }
                ]
            };

            var pipeline = TranspilerUtility.CreateTransformerPipeline(config, tempDir);
            var file = TranspilerUtility.ParseAndTransformTree(
                "class Sample {}",
                new RojoProject(),
                config,
                Path.Combine(tempDir, "Sample.cs"),
                pipeline,
                tempDir);

            var root = (CompilationUnitSyntax)file.Tree.GetRoot();
            Assert.Contains(root.GetLeadingTrivia(), trivia => trivia.ToString().Contains("sample-plugin:integration", StringComparison.Ordinal));
            Assert.True(root.Usings.Count >= 5, "Built-in transformer should still add implicit usings.");
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

}
