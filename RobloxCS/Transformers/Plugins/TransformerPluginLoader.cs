using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RobloxCS.Luau;
using RobloxCS.Shared;

namespace RobloxCS.Transformers.Plugins;

using TransformMethod = Func<FileCompilation, Microsoft.CodeAnalysis.SyntaxTree>;

public static class TransformerPluginLoader
{
    public static IReadOnlyList<TransformerRegistration> Load(ConfigData config, string projectDirectory)
    {
        if (config.Plugins.Count == 0)
        {
            return Array.Empty<TransformerRegistration>();
        }

        var registrations = new List<TransformerRegistration>();

        foreach (var pluginConfig in config.Plugins)
        {
            if (string.IsNullOrWhiteSpace(pluginConfig.Assembly))
            {
                continue;
            }

            var assemblyPath = GetPluginAssemblyPath(pluginConfig.Assembly, projectDirectory);
            if (assemblyPath is null)
            {
                Logger.Warn($"Transformer plugin '{pluginConfig.Assembly}' could not be resolved.");
                continue;
            }

            if (!File.Exists(assemblyPath))
            {
                Logger.Warn($"Transformer plugin assembly not found at '{assemblyPath}'.");
                continue;
            }

            try
            {
                var assembly = Assembly.LoadFrom(assemblyPath);
                var pluginType = ResolvePluginType(assembly, pluginConfig.Type);
                if (pluginType is null)
                {
                    Logger.Warn($"Transformer plugin type '{pluginConfig.Type ?? "<auto>"}' was not found in '{assemblyPath}'.");
                    continue;
                }

                if (Activator.CreateInstance(pluginType) is not ITransformerPlugin pluginInstance)
                {
                    Logger.Warn($"Failed to instantiate transformer plugin '{pluginType.FullName}'.");
                    continue;
                }

                var context = new TransformerPluginContext(config, pluginConfig, projectDirectory);
                var pluginRegistrations = pluginInstance.CreateTransformers(context);

                if (pluginRegistrations == null)
                {
                    continue;
                }

                foreach (var registration in pluginRegistrations)
                {
                    if (registration.Transform is null)
                    {
                        continue;
                    }

                    registrations.Add(registration);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to load transformer plugin '{pluginConfig.Assembly}': {ex.Message}");
            }
        }

        return registrations;
    }

    private static string? GetPluginAssemblyPath(string assembly, string projectDirectory)
    {
        if (Path.IsPathRooted(assembly))
        {
            return Path.GetFullPath(assembly);
        }

        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(projectDirectory, assembly));
    }

    private static Type? ResolvePluginType(Assembly assembly, string? explicitTypeName)
    {
        if (!string.IsNullOrWhiteSpace(explicitTypeName))
        {
            return assembly.GetType(explicitTypeName, throwOnError: false, ignoreCase: false);
        }

        return assembly
            .GetTypes()
            .FirstOrDefault(type =>
                typeof(ITransformerPlugin).IsAssignableFrom(type)
                && !type.IsAbstract
                && type.GetConstructor(Type.EmptyTypes) is not null);
    }
}
