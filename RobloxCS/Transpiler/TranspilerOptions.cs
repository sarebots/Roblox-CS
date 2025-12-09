using RobloxCS.Shared;

namespace RobloxCS.TranspilerV2;

public sealed record TranspilerOptions(ScriptType ScriptType, MacroOptions MacroOptions, RojoProject? RojoProject = null);
