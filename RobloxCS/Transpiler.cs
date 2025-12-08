using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2;
using Path = System.IO.Path;

namespace RobloxCS;

public sealed record TranspileResult(
    string ProjectDirectory,
    string SourceDirectory,
    string OutputDirectory,
    string? RojoProjectPath,
    string RojoProjectName,
    ConfigData Config);

/// <summary>This class contains everything needed to transpile C# to Luau.</summary>
public static class Transpiler
{
    private static readonly HashSet<string> _ignoredDiagnostics = new(StringComparer.OrdinalIgnoreCase)
    {
        "CS5001"
    };

    public static TranspileResult Transpile(string directoryPath, ConfigData config, bool verbose)
    {
        var rojoProject = RojoReader.ReadFromDirectory(directoryPath, config.RojoProjectName);
        if (rojoProject == null)
            throw Logger.Error("Rojo project name 'UNIT_TESTING' is reserved for internal use.");

        var rojoProjectPath = RojoReader.FindProjectPath(directoryPath, config.RojoProjectName);
        if (string.IsNullOrEmpty(rojoProjectPath))
            throw Logger.Error($"Failed to locate Rojo project '{config.RojoProjectName}.project.json' in '{directoryPath}'.");

        var sourceDirectory = Path.Join(directoryPath, config.SourceFolder);
        var outputDirectory = Path.Join(directoryPath, config.OutputFolder);
        var sourceFilePaths = Directory.GetFiles(sourceDirectory,
            "*.cs",
            SearchOption.AllDirectories);

        // this is prettyyy ass
        foreach (var (sourcePath, output) in TranspileSources(sourceFilePaths, rojoProject, config, directoryPath))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            var outputPath = Path.Combine(outputDirectory, Path.ChangeExtension(relativePath, ".luau"));
            if (verbose)
                Logger.Info($"Transpiling '{Path.GetRelativePath(directoryPath, sourcePath)}' into '{Path.GetRelativePath(directoryPath, outputPath)}'...");

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            
            File.WriteAllText(outputPath, output);
        }

        CopyRuntimeAssets(outputDirectory);

        return new TranspileResult(
            directoryPath,
            sourceDirectory,
            outputDirectory,
            rojoProjectPath,
            config.RojoProjectName,
            config
        );
    }
    
    public static List<(string Path, string Output)> TranspileSources(
        IEnumerable<string> sourceFilePaths,
        RojoProject rojoProject,
        ConfigData config,
        string? projectDirectory = null)
    {
        try
        {
            var resolvedProjectDirectory = projectDirectory ?? config.ProjectRoot ?? Directory.GetCurrentDirectory();
            var pipeline = TranspilerUtility.CreateTransformerPipeline(config, resolvedProjectDirectory);

            var files = sourceFilePaths
                .Select(path => (Path: path, Compilation: TranspilerUtility.ParseAndTransformTree(
                    File.ReadAllText(path),
                    rojoProject,
                    config,
                    path,
                    pipeline,
                    resolvedProjectDirectory)))
                .ToList();
            
            var trees = files.ConvertAll(file => file.Compilation.Tree);
            var compiler = TranspilerUtility.GetCompiler(trees, config);
            var diagnostics = compiler.GetDiagnostics()
                                      .Where(diagnostic => !_ignoredDiagnostics.Contains(diagnostic.Id))
                                      .ToList();

            if (diagnostics.Count > 0)
            {
                var hasErrors = false;
                var previousExitFlag = Logger.Exit;
                Logger.Exit = false;
                try
                {
                    foreach (var diagnostic in diagnostics)
                    {
                        if (diagnostic.Severity == DiagnosticSeverity.Error
                         || (diagnostic.Severity == DiagnosticSeverity.Warning && diagnostic.IsWarningAsError))
                        {
                            hasErrors = true;
                        }

                        Logger.HandleDiagnostic(diagnostic);
                    }
                }
                finally
                {
                    Logger.Exit = previousExitFlag;
                }

                PrintDiagnosticSummary(diagnostics);

                if (hasErrors)
                {
                    RunAnalyzerForCompilationErrors(files, compiler);

                    if (previousExitFlag)
                    {
                        Logger.Error("Transpilation failed due to analyzer diagnostics.");
                    }
                    else
                    {
                        throw new CleanExitException("Transpilation failed due to analyzer diagnostics.");
                    }
                }
            }

            // Always run the analyzer to surface roblox-cs diagnostics even when Roslyn reports none.
            RunAnalyzerForCompilationErrors(files, compiler);

            return files.ConvertAll(file =>
            {
                try
                {
                    var chunk = TranspilerUtility.GetLuauChunkV2(file.Compilation, compiler, ScriptType.Module);
                    var output = TranspilerUtility.RenderLuauChunkV2(chunk);
                    return (file.Path, output);
                }
                catch (CleanExitException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Fall back to the legacy emitter if V2 cannot handle this file yet.
                    Logger.Warn($"Falling back to legacy emit for '{file.Path}' ({ex.GetType().Name}): {ex.Message}");
                    return (file.Path, TranspilerUtility.GenerateLuau(file.Compilation, compiler));
                }
            });
        }
        catch (CleanExitException)
        {
            return [];
        }
        finally
        {
            Logger.ResetDiagnosticState();
        }
    }

    private static void RunAnalyzerForCompilationErrors(IEnumerable<(string Path, RobloxCS.Luau.FileCompilation Compilation)> files, CSharpCompilation compiler)
    {
        foreach (var (_, compilation) in files)
        {
            var analyzer = new Analyzer(compilation, compiler);
            analyzer.Analyze(compilation.Tree.GetRoot());
        }
    }

    private static void PrintDiagnosticSummary(IEnumerable<Diagnostic> diagnostics)
    {
        var robloxDiagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id.StartsWith("ROBLOXCS", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (robloxDiagnostics.Count == 0)
        {
            return;
        }

        var groups = robloxDiagnostics
            .GroupBy(diagnostic => diagnostic.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        Logger.Info("Diagnostics summary:");
        foreach (var group in groups)
        {
            Logger.Info($"  {group.Key}: {group.Count()} occurrence(s)");
        }

        Logger.Info("Refer to docs/Diagnostics.md for detailed guidance.");

        var overrideDocs = Logger.GetOverrideDocs(groups.Select(group => group.Key)).ToList();
        if (overrideDocs.Count > 0)
        {
            foreach (var doc in overrideDocs)
            {
                Logger.Info($"See {doc} for additional guidance.");
            }
        }
    }

    private static void CopyRuntimeAssets(string outputDirectory)
    {
        try
        {
            var repoRoot = LocateRepositoryRoot();
            var includeSource = Path.Combine(repoRoot, "roblox-cs", "RobloxCS", RobloxCS.Shared.Constants.IncludeFolderName);
            var runtimeSource = Path.Combine(repoRoot, "roblox-cs", "RobloxCS.Runtime");

            CopyDirectory(includeSource, Path.Combine(outputDirectory, RobloxCS.Shared.Constants.IncludeFolderName));
            CopyDirectory(runtimeSource, Path.Combine(outputDirectory, "RobloxCS.Runtime"));
        }
        catch (CleanExitException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Warn($"Failed to copy runtime assets: {exception.Message}");
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            throw Logger.Error($"Runtime asset directory missing at '{source}'.");
        }

        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "roblox-cs")) &&
                Directory.Exists(Path.Combine(directory.FullName, "roblox-cs", "RobloxCS.Runtime")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw Logger.Error("Unable to locate repository root when copying runtime assets.");
    }
}
