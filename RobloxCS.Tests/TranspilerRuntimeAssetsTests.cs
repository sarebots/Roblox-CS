using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.Tests;

public sealed class TranspilerRuntimeAssetsTests
{
    [Fact]
    public void TranspileCopiesRuntimeAssetsIntoOutputDirectory()
    {
        var projectDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDirectory);

        try
        {
            var sourceFolder = Path.Combine(projectDirectory, "src");
            Directory.CreateDirectory(sourceFolder);

            File.WriteAllText(
                Path.Combine(projectDirectory, "default.project.json"),
                """
                {
                  "name": "default",
                  "tree": {
                    "$className": "DataModel",
                    "ServerScriptService": {
                      "$className": "ServerScriptService",
                      "Scripts": {
                        "$className": "Folder",
                        "$path": "src"
                      }
                    }
                  }
                }
                """);

            var config = new ConfigData
            {
                SourceFolder = "src",
                OutputFolder = "out",
                EntryPointArguments = Array.Empty<object>(),
                RojoProjectName = "default",
                RojoServePort = ConfigData.DefaultRojoPort,
            };

            var result = RobloxCS.Transpiler.Transpile(projectDirectory, config, verbose: false);

            var includePath = Path.Combine(result.OutputDirectory, RobloxCS.Shared.Constants.IncludeFolderName, "RuntimeLib.luau");
            var runtimePromisePath = Path.Combine(result.OutputDirectory, "RobloxCS.Runtime", "Promise.lua");

            Assert.True(File.Exists(includePath), "RuntimeLib.luau should be copied to the output Include directory.");
            Assert.True(File.Exists(runtimePromisePath), "Promise runtime should be copied to the output RobloxCS.Runtime directory.");
        }
        finally
        {
            if (Directory.Exists(projectDirectory))
            {
                Directory.Delete(projectDirectory, recursive: true);
            }
        }
    }
}
