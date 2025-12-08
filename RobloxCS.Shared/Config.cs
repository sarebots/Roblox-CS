using System.Collections.Generic;

namespace RobloxCS.Shared;

public sealed record class ConfigData
{
    public const int DefaultRojoPort = 34872;

    public required string SourceFolder { get; init; }
    public required string OutputFolder { get; init; }
    public required object[] EntryPointArguments { get; init; } = [];
    public required string RojoProjectName { get; init; } = "default";
    public int RojoServePort { get; init; } = DefaultRojoPort;
    public string? ProjectRoot { get; init; }
    public List<TransformerPluginConfig> Plugins { get; init; } = [];
    public bool EnableUnityAliases { get; init; }
    public MacroOptions Macro { get; init; } = new();

    public bool IsValid() =>
        !string.IsNullOrEmpty(SourceFolder)
        && !string.IsNullOrEmpty(OutputFolder)
        && !string.IsNullOrEmpty(RojoProjectName)
        && RojoServePort > 0
        && Plugins.TrueForAll(static plugin => !string.IsNullOrWhiteSpace(plugin.Assembly));
}

public sealed record class TransformerPluginConfig
{
    public required string Assembly { get; init; }
    public string? Type { get; init; }
    public bool After { get; init; }
    public Dictionary<string, string> Settings { get; init; } = new();
    public List<string> Watch { get; init; } = [];
}
