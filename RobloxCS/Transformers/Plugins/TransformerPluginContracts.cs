using System;
using System.Collections.Generic;
using RobloxCS.Luau;
using RobloxCS.Shared;

namespace RobloxCS.Transformers.Plugins;

using TransformMethod = Func<FileCompilation, Microsoft.CodeAnalysis.SyntaxTree>;

public interface ITransformerPlugin
{
    IEnumerable<TransformerRegistration> CreateTransformers(TransformerPluginContext context);
}

public sealed record TransformerPluginContext(
    ConfigData Config,
    TransformerPluginConfig PluginConfig,
    string ProjectDirectory);

public enum TransformerPhase
{
    BeforeBuiltIn,
    AfterBuiltIn,
}

public sealed record TransformerRegistration(TransformMethod Transform, TransformerPhase Phase)
{
    public static TransformerRegistration Before(TransformMethod transform) =>
        new(transform, TransformerPhase.BeforeBuiltIn);

    public static TransformerRegistration After(TransformMethod transform) =>
        new(transform, TransformerPhase.AfterBuiltIn);
}
