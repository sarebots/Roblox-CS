using CommandLine;

namespace RobloxCS.CLI;

internal sealed class NewCommandOptions
{
    [Value(0, MetaName = "template", Required = true, HelpText = "Template identifier to scaffold.")]
    public string TemplateId { get; init; } = string.Empty;

    [Option('d', "directory", Required = false, HelpText = "Destination directory for the scaffolded project. Defaults to a folder named after the template.")]
    public string? Directory { get; init; }

    [Option("with-runtime-tests", Required = false, HelpText = "Include runtime test scaffolding if available.")]
    public bool WithRuntimeTests { get; init; }

    [Option("dry-run", Required = false, HelpText = "Show planned scaffold operations without writing files.")]
    public bool DryRun { get; init; }

    [Option("name", Required = false, HelpText = "Display name to use for the scaffolded project. Defaults to the destination folder name.")]
    public string? ProjectName { get; init; }

    [Option("verify", Required = false, HelpText = "Run a transpile pass after scaffolding to ensure the project builds cleanly.")]
    public bool Verify { get; init; }

    [Option("verify-rojo", Required = false, HelpText = "Run `rojo build` during verification (implies --verify).")]
    public bool VerifyRojo { get; init; }
}
