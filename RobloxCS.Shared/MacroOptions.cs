namespace RobloxCS.Shared;

public sealed record MacroOptions
{
    public bool EnableIteratorHelpers { get; init; } = true;
    public bool EnableMathMacros { get; init; } = true;
    public bool EnableBit32Macros { get; init; } = true;
}
