using System;
using System.IO;
using System.Text.Json;
using RobloxCS.CLI.Templates;
using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.CLI.Tests;

public class TemplateScaffolderTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly bool _originalExit;

    public TemplateScaffolderTests()
    {
        _originalExit = Logger.Exit;
        Logger.Exit = false;
        _tempRoot = Path.Combine(Path.GetTempPath(), $"robloxcs-template-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        finally
        {
            Logger.Exit = _originalExit;
        }
    }

    [Fact]
    public void ManifestLoad_ReturnsUnityTemplate()
    {
        var catalog = TemplateManifestLoader.Load();
        Assert.Contains(catalog.Templates, template => template.Id == "unity-roll-a-ball");

        var template = catalog.GetById("unity-roll-a-ball");
        Assert.True(Directory.Exists(template.SourcePath), $"Expected template directory '{template.SourcePath}' to exist.");
    }

    [Fact]
    public void CreateOptions_DefaultsProjectNameAndVerification()
    {
        var options = TemplateScaffolder.CreateOptions(null, includeRuntimeTests: false, verifyTranspile: true, verifyRojoBuild: true, dryRun: false);
        Assert.Equal("RobloxProject", options.ProjectName);
        Assert.True(options.VerifyTranspile);
        Assert.True(options.VerifyRojoBuild);
    }

    [Fact]
    public void DryRun_DoesNotCreateDestination()
    {
        var catalog = TemplateManifestLoader.Load();
        var template = catalog.GetById("unity-roll-a-ball");
        var targetDirectory = Path.Combine(_tempRoot, "dry-run");

        var options = TemplateScaffolder.CreateOptions("Sample", includeRuntimeTests: true, verifyTranspile: true, verifyRojoBuild: false, dryRun: true);
        TemplateScaffolder.Scaffold(template, targetDirectory, options);

        Assert.False(Directory.Exists(targetDirectory));
    }

    [Fact]
    public void Scaffold_CopiesTemplateFiles()
    {
        var catalog = TemplateManifestLoader.Load();
        var template = catalog.GetById("unity-roll-a-ball");
        var targetDirectory = Path.Combine(_tempRoot, "scaffold");

        var options = TemplateScaffolder.CreateOptions("Sample", includeRuntimeTests: false, verifyTranspile: false, verifyRojoBuild: false, dryRun: false);
        TemplateScaffolder.Scaffold(template, targetDirectory, options);

        var copiedReadme = Path.Combine(targetDirectory, "README.md");
        Assert.True(File.Exists(copiedReadme), $"Expected README.md to be scaffolded to '{copiedReadme}'.");
    }

    [Fact]
    public void Scaffold_DestinationNotEmpty_Throws()
    {
        var catalog = TemplateManifestLoader.Load();
        var template = catalog.GetById("unity-roll-a-ball");
        var targetDirectory = Path.Combine(_tempRoot, "non-empty");
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(Path.Combine(targetDirectory, "keep.txt"), "occupied");

        var options = TemplateScaffolder.CreateOptions("Sample", includeRuntimeTests: false, verifyTranspile: false, verifyRojoBuild: false, dryRun: false);
        var exception = Assert.Throws<CleanExitException>(() => TemplateScaffolder.Scaffold(template, targetDirectory, options));
        Assert.Contains("not empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scaffold_CopiesAssetsIntoOutputFolder()
    {
        var catalog = TemplateManifestLoader.Load();
        var template = catalog.GetById("unity-roll-a-ball");
        var targetDirectory = Path.Combine(_tempRoot, "assets-copy");

        var options = TemplateScaffolder.CreateOptions("Sample Game", includeRuntimeTests: false, verifyTranspile: false, verifyRojoBuild: false, dryRun: false);
        TemplateScaffolder.Scaffold(template, targetDirectory, options);

        var templateAsset = Path.Combine(targetDirectory, "assets", "Rooms", "StarterRoom.json");
        Assert.True(File.Exists(templateAsset), $"Expected template asset to exist at '{templateAsset}'.");
        var templateAssetContents = File.ReadAllText(templateAsset);
        Assert.DoesNotContain("__PROJECT_NAME__", templateAssetContents);
        Assert.DoesNotContain("__PROJECT_NAMESPACE__", templateAssetContents);

        var outputAsset = Path.Combine(targetDirectory, "out", "assets", "Rooms", "StarterRoom.json");
        Assert.True(File.Exists(outputAsset), $"Expected copied asset to exist at '{outputAsset}'.");
        var outputAssetContents = File.ReadAllText(outputAsset);
        Assert.Equal(templateAssetContents, outputAssetContents);
        Assert.Contains("SampleGameStarterRoom", outputAssetContents);
        Assert.Contains("Sample Game", outputAssetContents);
    }

    [Fact]
    public void Verify_FailsWhenSourceFolderMissing()
    {
        var catalog = TemplateManifestLoader.Load();
        var template = catalog.GetById("unity-roll-a-ball");
        var targetDirectory = Path.Combine(_tempRoot, "missing-src");

        var scaffoldOptions = TemplateScaffolder.CreateOptions("Broken Project", includeRuntimeTests: false, verifyTranspile: false, verifyRojoBuild: false, dryRun: false);
        TemplateScaffolder.Scaffold(template, targetDirectory, scaffoldOptions);

        var originalSource = Path.Combine(targetDirectory, "src");
        var renamedSource = Path.Combine(targetDirectory, "src-misconfigured");
        Directory.Move(originalSource, renamedSource);

        var verifyOptions = TemplateScaffolder.CreateOptions("Broken Project", includeRuntimeTests: false, verifyTranspile: true, verifyRojoBuild: false, dryRun: false);
        var exception = Assert.Throws<CleanExitException>(() => TemplateVerifier.Verify(template, targetDirectory, verifyOptions));
        Assert.Contains("Template verification failed", exception.Message, StringComparison.OrdinalIgnoreCase);

        var summaryPath = Path.Combine(targetDirectory, TemplateVerifier.SummaryFileName);
        Assert.True(File.Exists(summaryPath));
        var logPath = Path.Combine(targetDirectory, "roblox-cs.verification.log");
        Assert.True(File.Exists(logPath));

        using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
        Assert.False(summary.RootElement.GetProperty("succeeded").GetBoolean());
        var steps = summary.RootElement.GetProperty("steps");
        Assert.Contains(steps.EnumerateArray(), step =>
        {
            var status = step.GetProperty("status").GetString();
            return string.Equals(step.GetProperty("name").GetString(), "transpile", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase);
        });
        var error = summary.RootElement.GetProperty("errorMessage").GetString();
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Contains("src", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scaffold_RewritesProjectTokens()
    {
        var catalog = TemplateManifestLoader.Load();
        var template = catalog.GetById("unity-roll-a-ball");
        var targetDirectory = Path.Combine(_tempRoot, "tokens");

        var options = TemplateScaffolder.CreateOptions("Roller Quest", includeRuntimeTests: false, verifyTranspile: false, verifyRojoBuild: false, dryRun: false);
        TemplateScaffolder.Scaffold(template, targetDirectory, options);

        var configPath = Path.Combine(targetDirectory, "roblox-cs.yml");
        Assert.True(File.Exists(configPath));
        var configContents = File.ReadAllText(configPath);
        Assert.DoesNotContain("__PROJECT_NAME__", configContents);
        Assert.DoesNotContain("__PROJECT_NAMESPACE__", configContents);

        var sourcePath = Path.Combine(targetDirectory, "src", "EntryPoint.cs");
        Assert.True(File.Exists(sourcePath));
        var sourceContents = File.ReadAllText(sourcePath);
        Assert.Contains("Roller Quest", sourceContents);
        Assert.Contains("RollerQuest", sourceContents);
    }

    [Fact]
    public void Scaffold_WithVerify_TranspilesSuccessfully()
    {
        var catalog = TemplateManifestLoader.Load();
        var template = catalog.GetById("unity-roll-a-ball");
        var targetDirectory = Path.Combine(_tempRoot, "verify");

        var options = TemplateScaffolder.CreateOptions("Verified", includeRuntimeTests: false, verifyTranspile: true, verifyRojoBuild: false, dryRun: false);
        TemplateScaffolder.Scaffold(template, targetDirectory, options);

        var summaryPath = Path.Combine(targetDirectory, TemplateVerifier.SummaryFileName);
        Assert.True(File.Exists(summaryPath));
        var logPath = Path.Combine(targetDirectory, "roblox-cs.verification.log");
        Assert.True(File.Exists(logPath));

        var outputDir = Path.Combine(targetDirectory, "out");
        Assert.True(Directory.Exists(outputDir));
        Assert.True(Directory.GetFiles(outputDir, "*.luau", SearchOption.AllDirectories).Length > 0);

        var csprojPath = Path.Combine(targetDirectory, "Project", "RollABall.csproj");
        Assert.True(File.Exists(csprojPath));
        var csprojContents = File.ReadAllText(csprojPath);
        Assert.Contains("<RootNamespace>Verified</RootNamespace>", csprojContents);

        var assemblyInfoPath = Path.Combine(targetDirectory, "Project", "Properties", "AssemblyInfo.cs");
        Assert.True(File.Exists(assemblyInfoPath));
        var assemblyInfoContents = File.ReadAllText(assemblyInfoPath);
        Assert.DoesNotContain("__PROJECT_ASSEMBLY_TITLE__", assemblyInfoContents);
        Assert.DoesNotContain("__PROJECT_ASSEMBLY_NAME__", assemblyInfoContents);

        using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
        Assert.True(summary.RootElement.GetProperty("succeeded").GetBoolean());
        Assert.True(summary.RootElement.GetProperty("durationMs").GetDouble() >= 0);
        Assert.Equal("Verified", summary.RootElement.GetProperty("projectName").GetString());
        var steps = summary.RootElement.GetProperty("steps");
        Assert.Contains(steps.EnumerateArray(), step =>
            string.Equals(step.GetProperty("name").GetString(), "transpile", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(step.GetProperty("status").GetString(), "Success", StringComparison.OrdinalIgnoreCase));
        var metadata = summary.RootElement.GetProperty("projectMetadata");
        var projectRoot = metadata.GetProperty("projectRoot").GetString();
        Assert.False(string.IsNullOrEmpty(projectRoot));
        Assert.Equal("verify", Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar)));
        Assert.False(string.IsNullOrWhiteSpace(metadata.GetProperty("sourceDirectory").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(metadata.GetProperty("outputDirectory").GetString()));
        Assert.True(metadata.GetProperty("namespaceHints").GetArrayLength() > 0);
        Assert.True(metadata.GetProperty("assemblyHints").GetArrayLength() > 0);
    }

    [Fact]
    public void Verify_FailsWhenPlaceholdersRemainInOutput()
    {
        var catalog = TemplateManifestLoader.Load();
        var template = catalog.GetById("unity-roll-a-ball");
        var targetDirectory = Path.Combine(_tempRoot, "verify-placeholder");

        var scaffoldOptions = TemplateScaffolder.CreateOptions("Placeholder Project", includeRuntimeTests: false, verifyTranspile: false, verifyRojoBuild: false, dryRun: false);
        TemplateScaffolder.Scaffold(template, targetDirectory, scaffoldOptions);

        var placeholderFile = Path.Combine(targetDirectory, "out", "residual.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(placeholderFile)!);
        File.WriteAllText(placeholderFile, "This should be replaced: __PROJECT_NAME__");

        var verifyOptions = TemplateScaffolder.CreateOptions("Placeholder Project", includeRuntimeTests: false, verifyTranspile: true, verifyRojoBuild: false, dryRun: false);
        var exception = Assert.Throws<CleanExitException>(() => TemplateVerifier.Verify(template, targetDirectory, verifyOptions));
        Assert.Contains("placeholders", exception.Message, StringComparison.OrdinalIgnoreCase);

        var summaryPath = Path.Combine(targetDirectory, TemplateVerifier.SummaryFileName);
        Assert.True(File.Exists(summaryPath));
        using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
        Assert.False(summary.RootElement.GetProperty("succeeded").GetBoolean());
        Assert.NotNull(summary.RootElement.GetProperty("errorMessage").GetString());
    }

    [Fact]
    public void Scaffold_WithRojoVerify_FailureProducesSummary()
    {
        var original = Environment.GetEnvironmentVariable("ROBLOX_CS_FORCE_ROJO_RESULT");
        Environment.SetEnvironmentVariable("ROBLOX_CS_FORCE_ROJO_RESULT", "fail");

        try
        {
            var catalog = TemplateManifestLoader.Load();
            var template = catalog.GetById("unity-roll-a-ball");
            var targetDirectory = Path.Combine(_tempRoot, "verify-rojo-fail");

            var options = TemplateScaffolder.CreateOptions("Verified", includeRuntimeTests: false, verifyTranspile: true, verifyRojoBuild: true, dryRun: false);
            var exception = Assert.Throws<CleanExitException>(() => TemplateScaffolder.Scaffold(template, targetDirectory, options));
            Assert.Contains("rojo", exception.Message, StringComparison.OrdinalIgnoreCase);

            var summaryPath = Path.Combine(targetDirectory, TemplateVerifier.SummaryFileName);
            Assert.True(File.Exists(summaryPath));
            var logPath = Path.Combine(targetDirectory, "roblox-cs.verification.log");
            Assert.True(File.Exists(logPath));

            using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
            Assert.False(summary.RootElement.GetProperty("succeeded").GetBoolean());
            var steps = summary.RootElement.GetProperty("steps");
            Assert.Contains(steps.EnumerateArray(), step =>
            {
                var name = step.GetProperty("name").GetString();
                var status = step.GetProperty("status").GetString();
                return string.Equals(name, "rojo_build", StringComparison.OrdinalIgnoreCase)
                       && string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase);
            });
            Assert.NotNull(summary.RootElement.GetProperty("errorMessage").GetString());
            Assert.True(summary.RootElement.GetProperty("durationMs").GetDouble() >= 0);
            var metadata = summary.RootElement.GetProperty("projectMetadata");
            Assert.False(string.IsNullOrWhiteSpace(metadata.GetProperty("sourceDirectory").GetString()));
            Assert.True(metadata.GetProperty("namespaceHints").GetArrayLength() > 0);
            Assert.True(metadata.GetProperty("assemblyHints").GetArrayLength() > 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROBLOX_CS_FORCE_ROJO_RESULT", original);
        }
    }
}
