using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RobloxCS.Shared;
using RobloxCS;

namespace RobloxCS.CLI.Templates;

internal static class TemplateVerifier
{
    internal const string SummaryFileName = "roblox-cs.verification.json";

    public static string Verify(TemplateDefinition template, string projectDirectory, TemplateScaffoldOptions options)
    {
        Logger.Info("Verifying scaffolded project...");

        var summary = new VerificationSummary
        {
            TemplateId = template.Id,
            ProjectName = options.ProjectName,
            ProjectNamespace = options.ProjectNamespace,
            StartedAtUtc = DateTime.UtcNow,
        };
        var overallWatch = Stopwatch.StartNew();

        try
        {
            TranspileResult? result = null;

            if (options.VerifyTranspile)
            {
                result = RunTranspileStep(projectDirectory, options, summary);
            }

            if (options.VerifyRojoBuild)
            {
                if (result == null)
                {
                    result = RunTranspileStep(projectDirectory, options, summary);
                }

                RunRojoBuildStep(template, projectDirectory, result!, summary);
            }

            summary.Succeeded = true;
            summary.DurationMs = overallWatch.Elapsed.TotalMilliseconds;
            var summaryPath = WriteSummary(projectDirectory, summary);
            WriteLog(projectDirectory, summary);
            Logger.Ok("Template verification succeeded.");
            return summaryPath;
        }
        catch (CleanExitException)
        {
            if (!summary.Steps.Exists(step => step.Status == VerificationStepStatus.Failed))
            {
                summary.Succeeded = false;
                summary.ErrorMessage ??= "Verification aborted.";
            }

            summary.DurationMs = overallWatch.Elapsed.TotalMilliseconds;
            WriteSummary(projectDirectory, summary);
            WriteLog(projectDirectory, summary);
            throw;
        }
        catch (Exception ex)
        {
            summary.Succeeded = false;
            summary.ErrorMessage = ex.Message;
            summary.DurationMs = overallWatch.Elapsed.TotalMilliseconds;
            var summaryPath = WriteSummary(projectDirectory, summary);
            WriteLog(projectDirectory, summary);
            Logger.Warn($"Verification summary written to {summaryPath}");
            throw Logger.Error($"Template verification failed: {ex.Message}");
        }
    }

    private static TranspileResult RunTranspileStep(
        string projectDirectory,
        TemplateScaffoldOptions options,
        VerificationSummary summary)
    {
        static string FormatFailureMessage(IEnumerable<string> offendingFiles)
        {
            var list = offendingFiles.ToList();
            if (list.Count == 0)
            {
                return "Unresolved project placeholders remain in output files.";
            }

            var display = string.Join(", ", list);
            return $"Unresolved project placeholders remain in output files: {display}";
        }

        var step = summary.AddStep("transpile");
        try
        {
            var config = ConfigReader.Read(projectDirectory);
            var result = Transpiler.Transpile(projectDirectory, config, verbose: false);
            summary.Metadata.ProjectRoot = result.ProjectDirectory;
            summary.Metadata.SourceDirectory = result.SourceDirectory;
            summary.Metadata.OutputDirectory = result.OutputDirectory;
            summary.Metadata.RojoProjectName = result.RojoProjectName;
            summary.Metadata.SourceFolder = config.SourceFolder;
            summary.Metadata.OutputFolder = config.OutputFolder;
            summary.Metadata.EnableUnityAliases = config.EnableUnityAliases;
            summary.Metadata.ProjectNamespace = options.ProjectNamespace;
            PopulateNamespaceHints(result.ProjectDirectory, options.ProjectNamespace, summary.Metadata.NamespaceHints);
            PopulateAssemblyHints(result.ProjectDirectory, summary.Metadata.AssemblyHints, options.ProjectNamespace);
            if (!string.IsNullOrWhiteSpace(result.OutputDirectory) && Directory.Exists(result.OutputDirectory))
            {
                EnsureNoPlaceholderTokens(result.OutputDirectory);
            }
            step.MarkSuccess("Transpile completed successfully.");
            return result;
        }
        catch (CleanExitException ex)
        {
            step.MarkFailure(ex.Message);
            summary.ErrorMessage ??= ex.Message;
            WriteSummary(projectDirectory, summary);
            throw;
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrEmpty(summary.Metadata.OutputDirectory) && Directory.Exists(summary.Metadata.OutputDirectory))
            {
                var offending = FindFilesWithPlaceholders(summary.Metadata.OutputDirectory);
                if (offending.Count > 0)
                {
                    var message = FormatFailureMessage(offending);
                    step.MarkFailure(message);
                    summary.ErrorMessage ??= message;
                    WriteSummary(projectDirectory, summary);
                    throw Logger.Error(message);
                }
            }

            step.MarkFailure(ex.Message);
            summary.ErrorMessage ??= ex.Message;
            throw;
        }
    }

    private static void RunRojoBuildStep(
        TemplateDefinition template,
        string projectDirectory,
        TranspileResult result,
        VerificationSummary summary)
    {
        var step = summary.AddStep("rojo_build");

        var projectFile = result.RojoProjectPath;
        if (string.IsNullOrWhiteSpace(projectFile) || !File.Exists(projectFile))
        {
            var message = $"Template verification failed: expected Rojo project for '{template.Id}' but none was found.";
            step.MarkFailure(message);
            summary.ErrorMessage ??= message;
            WriteSummary(projectDirectory, summary);
            throw Logger.Error(message);
        }

        var outputFile = Path.Combine(result.OutputDirectory, $"{result.RojoProjectName}.rbxl");

        var forcedResult = Environment.GetEnvironmentVariable("ROBLOX_CS_FORCE_ROJO_RESULT");
        if (string.Equals(forcedResult, "skip", StringComparison.OrdinalIgnoreCase))
        {
            step.MarkSkipped("Rojo build verification skipped via ROBLOX_CS_FORCE_ROJO_RESULT.");
            return;
        }

        bool success;
        if (string.Equals(forcedResult, "fail", StringComparison.OrdinalIgnoreCase))
        {
            success = false;
        }
        else
        {
            success = RojoBuilder.RunBuild(projectFile, outputFile, projectDirectory, verbose: false);
        }

        if (!success)
        {
            var message = "`rojo build` did not complete successfully. Ensure `rojo` is installed and accessible on PATH.";
            step.MarkFailure(message);
            summary.ErrorMessage ??= message;
            WriteSummary(projectDirectory, summary);
            throw Logger.Error($"Template verification failed: {message}");
        }

        step.MarkSuccess($"rojo build verification succeeded ({outputFile}).");
        Logger.Ok("rojo build verification succeeded.");
    }

    private static string WriteSummary(string projectDirectory, VerificationSummary summary)
    {
        var summaryPath = Path.Combine(projectDirectory, SummaryFileName);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var json = JsonSerializer.Serialize(summary, options);
        File.WriteAllText(summaryPath, json);
        return summaryPath;
    }

    private static void WriteLog(string projectDirectory, VerificationSummary summary)
    {
        var logPath = Path.Combine(projectDirectory, "roblox-cs.verification.log");
        var builder = new StringBuilder();
        builder.AppendLine($"Template: {summary.TemplateId}");
        builder.AppendLine($"Project: {summary.ProjectName}");
        builder.AppendLine($"Namespace: {summary.ProjectNamespace}");
        builder.AppendLine($"Succeeded: {summary.Succeeded}");
        if (!string.IsNullOrWhiteSpace(summary.ErrorMessage))
        {
            builder.AppendLine($"Error: {summary.ErrorMessage}");
        }
        builder.AppendLine("Steps:");
        foreach (var step in summary.Steps)
        {
            builder.AppendLine($"  - {step.Name}: {step.Status} ({step.DurationMs:F2} ms)");
        }

        File.WriteAllText(logPath, builder.ToString());
    }

    private static void EnsureNoPlaceholderTokens(string outputDirectory)
    {
        var offending = FindFilesWithPlaceholders(outputDirectory);
        if (offending.Count > 0)
        {
            var message = $"Unresolved project placeholders remain in output files: {string.Join(", ", offending)}";
            throw Logger.Error(message);
        }
    }

    private static List<string> FindFilesWithPlaceholders(string rootDirectory)
    {
        var offendingFiles = new List<string>();
        var placeholders = new[]
        {
            "__PROJECT_NAME__",
            "__PROJECT_NAMESPACE__",
            "__PROJECT_ASSEMBLY_NAME__",
            "__PROJECT_ASSEMBLY_TITLE__"
        };
        var excludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".gif",
            ".bmp",
            ".rbxm",
            ".rbxl",
            ".mp3",
            ".ogg"
        };
        var excludedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SummaryFileName,
            "roblox-cs.verification.log"
        };

        foreach (var file in Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(file);
            if (excludedFileNames.Contains(fileName))
            {
                continue;
            }

            var extension = Path.GetExtension(file);
            if (!string.IsNullOrEmpty(extension) && excludedExtensions.Contains(extension))
            {
                continue;
            }

            string contents;
            try
            {
                contents = File.ReadAllText(file);
            }
            catch (Exception)
            {
                continue;
            }

            if (placeholders.Any(token => contents.Contains(token, StringComparison.Ordinal)))
            {
                offendingFiles.Add(Path.GetRelativePath(rootDirectory, file));
                if (offendingFiles.Count >= 10)
                {
                    break;
                }
            }
        }

        return offendingFiles;
    }

    private sealed class VerificationSummary
    {
        [JsonPropertyName("templateId")]
        public string TemplateId { get; init; } = string.Empty;

        [JsonPropertyName("projectName")]
        public string ProjectName { get; init; } = string.Empty;

        [JsonPropertyName("projectNamespace")]
        public string ProjectNamespace { get; init; } = string.Empty;

        [JsonPropertyName("startedAtUtc")]
        public DateTime StartedAtUtc { get; init; }

        [JsonPropertyName("durationMs")]
        public double DurationMs { get; set; }

        [JsonPropertyName("succeeded")]
        public bool Succeeded { get; set; }

        [JsonPropertyName("errorMessage")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("steps")]
        public List<VerificationStep> Steps { get; } = new();

        [JsonPropertyName("projectMetadata")]
        public ProjectMetadata Metadata { get; } = new();

        public VerificationStep AddStep(string name)
        {
            var step = new VerificationStep(name);
            Steps.Add(step);
            return step;
        }
    }

    private sealed class ProjectMetadata
    {
        [JsonPropertyName("projectRoot")]
        public string? ProjectRoot { get; set; }

        [JsonPropertyName("sourceDirectory")]
        public string? SourceDirectory { get; set; }

        [JsonPropertyName("outputDirectory")]
        public string? OutputDirectory { get; set; }

        [JsonPropertyName("rojoProjectName")]
        public string? RojoProjectName { get; set; }

        [JsonPropertyName("sourceFolder")]
        public string? SourceFolder { get; set; }

        [JsonPropertyName("outputFolder")]
        public string? OutputFolder { get; set; }

        [JsonPropertyName("enableUnityAliases")]
        public bool EnableUnityAliases { get; set; }

        [JsonPropertyName("projectNamespace")]
        public string ProjectNamespace { get; set; } = string.Empty;

        [JsonPropertyName("namespaceHints")]
        public List<string> NamespaceHints { get; } = new();

        [JsonPropertyName("assemblyHints")]
        public List<string> AssemblyHints { get; } = new();
    }

    private enum VerificationStepStatus
    {
        Pending,
        Success,
        Failed,
        Skipped,
    }

    private sealed class VerificationStep
    {
        public VerificationStep(string name)
        {
            Name = name;
            StartedAtUtc = DateTime.UtcNow;
            _watch = Stopwatch.StartNew();
        }

        private readonly Stopwatch _watch;

        [JsonPropertyName("name")]
        public string Name { get; }

        [JsonPropertyName("status")]
        public VerificationStepStatus Status { get; private set; } = VerificationStepStatus.Pending;

        [JsonPropertyName("message")]
        public string? Message { get; private set; }

        [JsonPropertyName("startedAtUtc")]
        public DateTime StartedAtUtc { get; }

        [JsonPropertyName("durationMs")]
        public double DurationMs { get; private set; }

        public void MarkSuccess(string message)
        {
            Status = VerificationStepStatus.Success;
            Message = message;
            DurationMs = _watch.Elapsed.TotalMilliseconds;
        }

        public void MarkFailure(string message)
        {
            Status = VerificationStepStatus.Failed;
            Message = message;
            DurationMs = _watch.Elapsed.TotalMilliseconds;
        }

        public void MarkSkipped(string message)
        {
            Status = VerificationStepStatus.Skipped;
            Message = message;
            DurationMs = _watch.Elapsed.TotalMilliseconds;
        }
    }

    private static void PopulateNamespaceHints(string projectRoot, string projectNamespace, List<string> hints)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || hints == null)
        {
            return;
        }

        var csprojFiles = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.AllDirectories);
        if (csprojFiles.Length > 0)
        {
            hints.Add($"Set <RootNamespace>{projectNamespace}</RootNamespace> in '{Path.GetFileName(csprojFiles[0])}'.");
        }
        else
        {
            hints.Add($"If you add a .csproj, set its RootNamespace to '{projectNamespace}'.");
        }

        var defaultNamespaceFiles = Directory.GetFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("namespace __PROJECT_NAMESPACE__", StringComparison.Ordinal))
            .ToList();

        if (defaultNamespaceFiles.Count > 0)
        {
            hints.Add("Replace any remaining '__PROJECT_NAMESPACE__' tokens in custom files with your chosen namespace.");
        }

        hints.Add("Update runtime spec namespaces (under tests/runtime) if you add additional ones so verification continues to pass.");
    }

    private static void PopulateAssemblyHints(string projectRoot, List<string> hints, string projectNamespace)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || hints == null)
        {
            return;
        }

        var assemblyInfoFiles = Directory.GetFiles(projectRoot, "*AssemblyInfo.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("AssemblyTitle", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (assemblyInfoFiles.Count > 0)
        {
            hints.Add($"Update AssemblyTitle/AssemblyProduct attributes in '{Path.GetFileName(assemblyInfoFiles[0])}' to reflect {projectNamespace}.");
        }
        else
        {
            hints.Add("Consider adding AssemblyInfo.cs (or equivalent assembly metadata) if you plan to distribute the generated project.");
        }

        var csprojFiles = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.AllDirectories);
        if (csprojFiles.Length > 0)
        {
            hints.Add($"Set <AssemblyName>{projectNamespace}</AssemblyName> in '{Path.GetFileName(csprojFiles[0])}' if you rename the project.");
        }
    }
}
