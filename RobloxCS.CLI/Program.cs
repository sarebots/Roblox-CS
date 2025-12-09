using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Timers;
using CommandLine;
using RobloxCS;
using RobloxCS.CLI.Templates;
using RobloxCS.Shared;

namespace RobloxCS.CLI;

internal static class Program
{
    public static void Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && string.Equals(args[0], "new", StringComparison.OrdinalIgnoreCase))
            {
                Parser.Default
                    .ParseArguments<NewCommandOptions>(args.Skip(1))
                    .WithParsed(HandleNewCommand);
                return;
            }

            Parser.Default
                .ParseArguments<Options>(args)
                .WithParsed(HandleOptions);
        }
        catch (CleanExitException)
        {
            Environment.Exit(1);
        }
    }

    private static void HandleOptions(Options opts)
    {
        if (opts.Version)
        {
            var assembly = typeof(Transpiler).Assembly;
            var informationalVersionAttribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            Console.WriteLine(informationalVersionAttribute?.InformationalVersion.Split('+').First());
            return;
        }

        if (opts.SingleFile != null)
        {
            var config = ApplyMacroOverrides(
                ConfigReader.UnitTestingConfig,
                opts.MacroIteratorHelpers,
                opts.MacroMath,
                opts.MacroBit32);

            var transpiledLuau = Transpiler.TranspileSources([opts.SingleFile],
                new RojoProject(),
                config);

            Console.WriteLine(transpiledLuau.First().Output);
            return;
        }

        var projectDirectory = ResolveProjectDirectory(opts.ProjectDirectory);
        if (!Directory.Exists(projectDirectory))
            throw Logger.Error($"Project directory does not exist at '{projectDirectory}'");

        var rojoOutput = ResolveRojoOutputPath(projectDirectory, opts.RojoBuildOutput);
        var rojoPortOverride = opts.RojoPort;

        var initialResult = RunTranspile(projectDirectory, opts.Verbose, opts.RojoBuild, rojoOutput, rojoPortOverride, opts.MacroIteratorHelpers, opts.MacroMath, opts.MacroBit32);

        if (initialResult != null && opts.RunRuntimeTests)
        {
            RuntimeSpecRunner.Run(initialResult, opts.Verbose);
        }

        if (initialResult != null && opts.Watch)
        {
            WatchProject(projectDirectory, opts.Verbose, opts.RojoBuild, rojoOutput, rojoPortOverride, opts.RunRuntimeTests, initialResult, opts.DisableRojoServe, opts.MacroIteratorHelpers, opts.MacroMath, opts.MacroBit32);
        }
    }

    private static void HandleNewCommand(NewCommandOptions opts)
    {
        try
        {
            var catalog = TemplateManifestLoader.Load();
            var template = catalog.GetById(opts.TemplateId);

            var destination = string.IsNullOrWhiteSpace(opts.Directory)
                ? Path.Combine(Directory.GetCurrentDirectory(), !string.IsNullOrWhiteSpace(opts.ProjectName) ? opts.ProjectName : template.Id)
                : Path.GetFullPath(opts.Directory);

            var inferredName = !string.IsNullOrWhiteSpace(opts.ProjectName)
                ? opts.ProjectName!.Trim()
                : Path.GetFileName(destination.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? template.Label;

            if (string.IsNullOrWhiteSpace(inferredName))
            {
                inferredName = template.Label;
            }

            Logger.Info($"Scaffolding template '{template.Id}' ({template.Label}).");
            if (!string.IsNullOrWhiteSpace(template.Description))
            {
                Logger.Info(template.Description);
            }

            var verifyTranspile = opts.Verify || opts.VerifyRojo;
            var scaffoldOptions = TemplateScaffolder.CreateOptions(
                inferredName,
                opts.WithRuntimeTests,
                verifyTranspile,
                opts.VerifyRojo,
                opts.DryRun);
            TemplateScaffolder.Scaffold(template, destination, scaffoldOptions);
        }
        catch (CleanExitException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Logger.Error($"Unhandled template scaffolding error: {ex.Message}");
        }
    }

    private sealed class Options
    {
        [Option('v', "version", Required = false, HelpText = "Returns the compiler version.")]
        public required bool Version { get; init; }

        [Option("verbose", Required = false, HelpText = "Verbosely outputs transpilation process.")]
        public required bool Verbose { get; init; }

        [Option('f', "single-file", Required = false, HelpText = "Transpiles a single file and spits the emitted Luau out into the console.")]
        public required string? SingleFile { get; init; }

        [Option('p', "project", Required = false, HelpText = "Explicitly specify the project directory to compile. If none specified, automatically attempts to locate one.")]
        public required string ProjectDirectory { get; init; } = ".";

        [Option('w', "watch", Required = false, HelpText = "Watches the project for changes and recompiles incrementally.")]
        public required bool Watch { get; init; }

        [Option("rojo-build", Required = false, HelpText = "Runs `rojo build` after each successful transpilation.")]
        public required bool RojoBuild { get; init; }

        [Option("rojo-build-output", Required = false, HelpText = "Optional path for `rojo build` output (.rbxl). Defaults to `<OutputFolder>/<RojoProjectName>.rbxl`.")]
        public required string? RojoBuildOutput { get; init; }

        [Option("rojo-port", Required = false, HelpText = "Overrides the Rojo serve port used for sync/watch.")]
        public required int? RojoPort { get; init; }

        [Option("no-serve", Required = false, HelpText = "Disables automatic `rojo serve` when using --watch.")]
        public required bool DisableRojoServe { get; init; }

        [Option("runtime-tests", Required = false, HelpText = "Runs the transpiled runtime specs via `lune run tests/runtime/run.lua`. Set ROBLOX_CS_LUNE_PATH to override the lune executable.")]
        public required bool RunRuntimeTests { get; init; }

        [Option("macro-iterator-helpers", Required = false, HelpText = "Override Macro.EnableIteratorHelpers (true/false) for this invocation (see docs/ts-iter-guardrails.md).")]
        public bool? MacroIteratorHelpers { get; init; }

        [Option("macro-math", Required = false, HelpText = "Override Macro.EnableMathMacros (true/false).")]
        public bool? MacroMath { get; init; }

        [Option("macro-bit32", Required = false, HelpText = "Override Macro.EnableBit32Macros (true/false).")]
        public bool? MacroBit32 { get; init; }
    }

    private static string ResolveProjectDirectory(string? path) =>
        string.IsNullOrWhiteSpace(path) ? Directory.GetCurrentDirectory() : Path.GetFullPath(path);

    private static string? ResolveRojoOutputPath(string projectDirectory, string? rojoOutput)
    {
        if (string.IsNullOrWhiteSpace(rojoOutput))
        {
            return null;
        }

        return Path.GetFullPath(rojoOutput, projectDirectory);
    }

    private static ConfigData ApplyMacroOverrides(ConfigData config, bool? iteratorMacroOverride, bool? mathMacroOverride, bool? bit32MacroOverride)
    {
        if (iteratorMacroOverride is null && mathMacroOverride is null && bit32MacroOverride is null)
        {
            return config;
        }

        var macro = config.Macro ?? new MacroOptions();
        if (iteratorMacroOverride.HasValue)
        {
            macro = macro with { EnableIteratorHelpers = iteratorMacroOverride.Value };
        }

        if (mathMacroOverride.HasValue)
        {
            macro = macro with { EnableMathMacros = mathMacroOverride.Value };
        }

        if (bit32MacroOverride.HasValue)
        {
            macro = macro with { EnableBit32Macros = bit32MacroOverride.Value };
        }

        return config with { Macro = macro };
    }

    private static TranspileResult? RunTranspile(string projectDirectory, bool verbose, bool runRojoBuild, string? rojoOutputPath, int? rojoPortOverride, bool? iteratorMacroOverride, bool? mathMacroOverride, bool? bit32MacroOverride)
    {
        try
        {
            var config = ConfigReader.Read(projectDirectory);
            var port = rojoPortOverride ?? config.RojoServePort;
            if (port <= 0)
            {
                port = ConfigData.DefaultRojoPort;
            }

            var effectiveConfig = ApplyMacroOverrides(
                config with { RojoServePort = port, ProjectRoot = projectDirectory },
                iteratorMacroOverride,
                mathMacroOverride,
                bit32MacroOverride);

            if (verbose)
            {
                Logger.Info($"Using Rojo serve port {port}.");
            }

            var result = Transpiler.Transpile(projectDirectory, effectiveConfig, verbose);
            Logger.Ok($"Transpiled project at '{projectDirectory}'.");
            if (runRojoBuild)
            {
                RunRojoBuild(result, verbose, rojoOutputPath, port);
            }

            return result;
        }
        catch (CleanExitException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"Unhandled transpilation error: {ex}");
            return null;
        }
    }

    private static void WatchProject(string projectDirectory, bool verbose, bool runRojoBuild, string? rojoOutputPath, int? rojoPortOverride, bool runRuntimeTests, TranspileResult initialResult, bool disableRojoServe, bool? iteratorMacroOverride, bool? mathMacroOverride, bool? bit32MacroOverride)
    {
        Logger.Info("Watching for changes. Press Ctrl+C to exit.");

        if (verbose && rojoPortOverride.HasValue)
        {
            Logger.Info($"Rojo serve port override: {rojoPortOverride.Value}.");
        }

        var timerLock = new object();
        var pending = false;
        var changeAccumulator = new ChangeAccumulator();

        using var timer = new System.Timers.Timer(250) { AutoReset = false };
        using var exitEvent = new ManualResetEventSlim(false);
        var watcherDisposables = new List<FileSystemWatcher>();
        using var serverManager = disableRojoServe ? null : new RojoServerManager();
        var currentResult = initialResult;

        RestartRojoServer(currentResult);

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            exitEvent.Set();
        };

        timer.Elapsed += (_, _) =>
        {
            var shouldRun = false;
            lock (timerLock)
            {
                shouldRun = pending;
                pending = false;
            }

            if (!shouldRun) return;

            var result = RunTranspile(projectDirectory, verbose, runRojoBuild, rojoOutputPath, rojoPortOverride, iteratorMacroOverride, mathMacroOverride, bit32MacroOverride);
            if (result != null)
            {
                currentResult = result;
                RestartRojoServer(currentResult);
                if (runRuntimeTests)
                {
                    RuntimeSpecRunner.Run(currentResult, verbose);
                }
                if (changeAccumulator.TryTakeSummary(out var summary))
                {
                    Logger.Info(summary);
                }
            }

            lock (timerLock)
            {
                if (pending)
                {
                    timer.Start();
                }
            }
        };

        void RecordChange(object? sender, FileSystemEventArgs args)
        {
            var fsWatcher = sender as FileSystemWatcher;
            var path = args.FullPath;
            if (fsWatcher != null && !string.IsNullOrEmpty(args.Name))
            {
                path = Path.Combine(fsWatcher.Path, args.Name);
            }

            changeAccumulator.Add(path);
        }

        void ScheduleRebuild()
        {
            lock (timerLock)
            {
                pending = true;
                timer.Stop();
                timer.Start();
            }
        }

        void AttachWatcher(FileSystemWatcher watcher)
        {
            FileSystemEventHandler handler = (sender, args) =>
            {
                RecordChange(sender, args);
                ScheduleRebuild();
            };
            RenamedEventHandler renameHandler = (sender, args) =>
            {
                RecordChange(sender, args);
                ScheduleRebuild();
            };
            watcher.Changed += handler;
            watcher.Created += handler;
            watcher.Deleted += handler;
            watcher.Renamed += renameHandler;
            watcher.EnableRaisingEvents = true;
            watcherDisposables.Add(watcher);
        }

        FileSystemWatcher? TryCreateWatcher(string directory, string filter, bool includeSubdirectories)
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }

            var watcher = new FileSystemWatcher(directory)
            {
                Filter = filter,
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName | NotifyFilters.CreationTime,
            };

            return watcher;
        }

        var sourceWatcher = TryCreateWatcher(projectDirectory, "*.cs", includeSubdirectories: true);
        if (sourceWatcher != null) AttachWatcher(sourceWatcher);

        var configWatcher = TryCreateWatcher(projectDirectory, "roblox-cs.yml", includeSubdirectories: false);
        if (configWatcher != null) AttachWatcher(configWatcher);

        var rojoWatcher = TryCreateWatcher(projectDirectory, "*.project.json", includeSubdirectories: false);
        if (rojoWatcher != null) AttachWatcher(rojoWatcher);

        var pluginWatchTargets = PluginWatchTargetBuilder.Build(initialResult.Config, projectDirectory);
        foreach (var target in pluginWatchTargets)
        {
            var pluginWatcher = TryCreateWatcher(target.Directory, target.Filter, target.IncludeSubdirectories);
            if (pluginWatcher != null)
            {
                AttachWatcher(pluginWatcher);
            }
        }

        if (watcherDisposables.Count == 0)
        {
            Logger.Warn("No watchable inputs found; exiting watch mode.");
            return;
        }

        exitEvent.Wait();

        foreach (var watcher in watcherDisposables)
        {
            watcher.Dispose();
        }

        serverManager?.Stop();

        void RestartRojoServer(TranspileResult? result)
        {
            if (serverManager == null)
            {
                return;
            }

            if (result?.RojoProjectPath == null)
            {
                return;
            }

            var port = result.Config.RojoServePort;
            serverManager.Start(result.RojoProjectPath, result.ProjectDirectory, port, verbose);
        }
    }

    private static void RunRojoBuild(TranspileResult result, bool verbose, string? rojoOutputPath, int rojoPort)
    {
        if (string.IsNullOrWhiteSpace(result.RojoProjectPath))
        {
            Logger.Warn("Skipping `rojo build` because no Rojo project file was found.");
            return;
        }

        var outputFile = rojoOutputPath ?? Path.Combine(result.OutputDirectory, $"{result.RojoProjectName}.rbxl");
        var outputFolder = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
        }

        if (verbose)
        {
            Logger.Info($"Running `rojo build` (serve port {rojoPort}) for '{result.RojoProjectPath}' -> '{outputFile}'.");
        }

        var success = RojoBuilder.RunBuild(result.RojoProjectPath!, outputFile, result.ProjectDirectory, verbose);
        if (success)
        {
            Logger.Ok($"rojo build completed: {outputFile}");
        }
    }
}
