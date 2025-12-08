using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RobloxCS.Shared;

namespace RobloxCS.CLI.Templates;

internal readonly record struct TemplateScaffoldOptions(
    string ProjectName,
    string ProjectNamespace,
    bool IncludeRuntimeTests,
    bool VerifyTranspile,
    bool VerifyRojoBuild,
    bool DryRun);

internal static class TemplateScaffolder
{
    private static readonly HashSet<string> TokenisedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".luau", ".lua", ".json", ".yml", ".yaml", ".md", ".txt"
    };

    public static TemplateScaffoldOptions CreateOptions(
        string? requestedName,
        bool includeRuntimeTests,
        bool verifyTranspile,
        bool verifyRojoBuild,
        bool dryRun)
    {
        var projectName = !string.IsNullOrWhiteSpace(requestedName)
            ? requestedName.Trim()
            : "RobloxProject";

        var projectNamespace = BuildNamespace(projectName);
        return new TemplateScaffoldOptions(projectName, projectNamespace, includeRuntimeTests, verifyTranspile, verifyRojoBuild, dryRun);
    }

    public static void Scaffold(TemplateDefinition template, string destinationDirectory, TemplateScaffoldOptions options)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw Logger.Error("Destination directory cannot be empty.");
        }

        var resolvedDestination = Path.GetFullPath(destinationDirectory);
        var destinationExists = Directory.Exists(resolvedDestination);
        var destinationEntries = destinationExists
            ? Directory.EnumerateFileSystemEntries(resolvedDestination).Take(1).Any()
            : false;

        if (destinationEntries)
        {
            throw Logger.Error($"Destination '{resolvedDestination}' is not empty. Provide an empty directory or a new path.");
        }

        if (options.DryRun)
        {
            Logger.Info($"[dry-run] Template '{template.Id}' would be copied to '{resolvedDestination}'.");
            Logger.Info($"[dry-run] Project name: {options.ProjectName}");
            Logger.Info($"[dry-run] Project namespace: {options.ProjectNamespace}");
            foreach (var sourceFile in Directory.EnumerateFiles(template.SourcePath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(template.SourcePath, sourceFile);
                Logger.Info($"[dry-run] Would copy {relativePath}");
            }

            if (options.IncludeRuntimeTests)
            {
                Logger.Info("[dry-run] Runtime test scaffolding requested (no files generated yet).");
            }
            if (options.VerifyTranspile)
            {
                Logger.Info("[dry-run] Verification skipped because no files are written.");
            }

            if (options.VerifyRojoBuild)
            {
                Logger.Info("[dry-run] Rojo verification skipped because no files are written.");
            }

            return;
        }

        Directory.CreateDirectory(resolvedDestination);

        foreach (var directory in Directory.EnumerateDirectories(template.SourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(template.SourcePath, directory);
            var destination = Path.Combine(resolvedDestination, relative);
            Directory.CreateDirectory(destination);
        }

        var deferredNamespaceFiles = new List<string>();

        foreach (var file in Directory.EnumerateFiles(template.SourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(template.SourcePath, file);
            var destination = Path.Combine(resolvedDestination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: false);

            TryRewriteTokens(destination, options);

            var extension = Path.GetExtension(destination);
            if (string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                InjectRootNamespace(destination, options.ProjectNamespace);
            }
            else if (string.Equals(Path.GetFileName(destination), "AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
            {
                InjectAssemblyAttributes(destination, options.ProjectNamespace);
            }
            else if (string.Equals(Path.GetFileName(destination), "Bootstrap.spec.cs", StringComparison.OrdinalIgnoreCase))
            {
                deferredNamespaceFiles.Add(destination);
            }
        }

        foreach (var path in deferredNamespaceFiles)
        {
            TryRewriteTokens(path, options);
        }

        TryCopyTemplateAssets(resolvedDestination);

        Logger.Ok($"Scaffolded template '{template.Id}' into '{resolvedDestination}'.");

        if (options.IncludeRuntimeTests)
        {
            Logger.Warn("Runtime test scaffolding is not implemented yet; enabling this flag currently performs no additional actions.");
        }

        string? summaryPath = null;
        if (options.VerifyTranspile || options.VerifyRojoBuild)
        {
            var originalExitFlag = Logger.Exit;
            Logger.Exit = false;
            try
            {
                summaryPath = TemplateVerifier.Verify(template, resolvedDestination, options);
            }
            finally
            {
                Logger.Exit = originalExitFlag;
            }
        }

        if (!string.IsNullOrEmpty(summaryPath))
        {
            Logger.Info($"Verification summary written to {summaryPath}");
        }
    }

    private static string BuildNamespace(string projectName)
    {
        var builder = new StringBuilder(projectName.Length);
        var capitalizeNext = true;

        foreach (var ch in projectName)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }

        var candidate = builder.Length > 0 ? builder.ToString() : "RobloxProject";
        if (!char.IsLetter(candidate[0]))
        {
            candidate = $"Project{candidate}";
        }

        return candidate;
    }

    private static void TryRewriteTokens(string path, TemplateScaffoldOptions options)
    {
        var extension = Path.GetExtension(path);
        if (!TokenisedExtensions.Contains(extension))
        {
            return;
        }

        var contents = File.ReadAllText(path);
        var updated = contents
            .Replace("__PROJECT_NAME__", options.ProjectName, StringComparison.Ordinal)
            .Replace("__PROJECT_NAMESPACE__", options.ProjectNamespace, StringComparison.Ordinal);

        if (!ReferenceEquals(contents, updated) && !string.Equals(contents, updated, StringComparison.Ordinal))
        {
            File.WriteAllText(path, updated);
        }
    }

    private static void TryCopyTemplateAssets(string projectRoot)
    {
        ConfigData? config;
        try
        {
            config = ConfigReader.Read(projectRoot);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Unable to load roblox-cs.yml while copying assets: {ex.Message}");
            return;
        }

        if (config == null || string.IsNullOrWhiteSpace(config.OutputFolder))
        {
            return;
        }

        var assetsSource = Path.Combine(projectRoot, "assets");
        if (!Directory.Exists(assetsSource))
        {
            return;
        }

        var outputRoot = Path.Combine(projectRoot, config.OutputFolder);
        var assetsDestination = Path.Combine(outputRoot, "assets");
        try
        {
            CopyDirectoryContents(assetsSource, assetsDestination);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to copy template assets to '{assetsDestination}': {ex.Message}");
        }
    }

    private static void CopyDirectoryContents(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(target);
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target))
            {
                File.Copy(file, target, overwrite: false);
            }
        }
    }

    private static void InjectRootNamespace(string csprojPath, string projectNamespace)
    {
        try
        {
            var contents = File.ReadAllText(csprojPath);
            if (contents.Contains("<RootNamespace>", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var insertion = $"  <RootNamespace>{projectNamespace}</RootNamespace>\n";
            contents = contents.Replace("<PropertyGroup>", "<PropertyGroup>\n" + insertion);
            File.WriteAllText(csprojPath, contents);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to inject RootNamespace into '{csprojPath}': {ex.Message}");
        }
    }

    private static void InjectAssemblyAttributes(string assemblyInfoPath, string projectNamespace)
    {
        try
        {
            var contents = File.ReadAllText(assemblyInfoPath);
            var replaced = contents
                .Replace("__PROJECT_ASSEMBLY_TITLE__", projectNamespace, StringComparison.Ordinal)
                .Replace("__PROJECT_ASSEMBLY_NAME__", projectNamespace, StringComparison.Ordinal);

            if (!ReferenceEquals(contents, replaced) && !string.Equals(contents, replaced, StringComparison.Ordinal))
            {
                File.WriteAllText(assemblyInfoPath, replaced);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to inject assembly attributes into '{assemblyInfoPath}': {ex.Message}");
        }
    }
}
