using Microsoft.CodeAnalysis;
using RobloxCS.Luau;
using RobloxCS.Shared;

namespace RobloxCS.Transformers;

using TransformMethod = Func<FileCompilation, SyntaxTree>;

public static class BuiltInTransformers
{
    public static TransformMethod Main() => file => new MainTransformer(file).TransformTree();
    public static TransformMethod UnityAliases() => file => new UnityAliasTransformer(file).TransformTree();

    private static TransformMethod FailedToGetTransformer(string name) => throw Logger.Error($"No built-in transformer \"{name}\" exists (roblox-cs.yml)");
}
