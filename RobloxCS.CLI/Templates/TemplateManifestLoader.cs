using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using RobloxCS.Shared;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RobloxCS.CLI.Templates;

internal sealed record TemplateDefinition(
    string Id,
    string Label,
    string Description,
    string SourcePath);

internal sealed class TemplateCatalog
{
    private readonly ImmutableDictionary<string, TemplateDefinition> _templatesById;

    public TemplateCatalog(string templatesRoot, IReadOnlyCollection<TemplateDefinition> templates)
    {
        TemplatesRoot = templatesRoot;
        Templates = templates;
        _templatesById = templates.ToImmutableDictionary(template => template.Id, template => template, StringComparer.OrdinalIgnoreCase);
    }

    public string TemplatesRoot { get; }

    public IReadOnlyCollection<TemplateDefinition> Templates { get; }

    public TemplateDefinition GetById(string id)
    {
        if (_templatesById.TryGetValue(id, out var template))
        {
            return template;
        }

        var available = Templates.Select(t => t.Id).OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
        throw Logger.Error($"Template '{id}' was not found. Available templates: {string.Join(", ", available)}");
    }
}

internal static class TemplateManifestLoader
{
    private const string TemplatesDirectoryName = "templates";
    private const string ManifestFileName = "index.yml";

    private sealed class ManifestDocument
    {
        public List<ManifestTemplate>? Templates { get; init; }
    }

    private sealed class ManifestTemplate
    {
        public string? Id { get; init; }
        public string? Label { get; init; }
        public string? Path { get; init; }
        public string? Description { get; init; }
    }

    public static TemplateCatalog Load()
    {
        var templatesRoot = ResolveTemplatesRoot();
        var manifestPath = Path.Combine(templatesRoot, ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            throw Logger.Error($"Template manifest not found at '{manifestPath}'.");
        }

        var manifestText = File.ReadAllText(manifestPath);
        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var document = deserializer.Deserialize<ManifestDocument>(manifestText)
                       ?? throw Logger.Error("Template manifest was empty or invalid.");

        if (document.Templates == null || document.Templates.Count == 0)
        {
            throw Logger.Error("Template manifest does not define any templates.");
        }

        var definitions = new List<TemplateDefinition>(document.Templates.Count);

        foreach (var entry in document.Templates)
        {
            if (string.IsNullOrWhiteSpace(entry?.Id))
            {
                throw Logger.Error("Template entry is missing an 'id' field.");
            }

            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                throw Logger.Error($"Template '{entry.Id}' is missing a 'path' field.");
            }

            var templatePath = Path.Combine(templatesRoot, entry.Path);
            if (!Directory.Exists(templatePath))
            {
                throw Logger.Error($"Template '{entry.Id}' expects directory '{templatePath}', but it does not exist.");
            }

            var label = string.IsNullOrWhiteSpace(entry.Label) ? entry.Id : entry.Label!;
            var description = entry.Description ?? string.Empty;
            definitions.Add(new TemplateDefinition(entry.Id!, label, description, Path.GetFullPath(templatePath)));
        }

        return new TemplateCatalog(templatesRoot, definitions);
    }

    private static string ResolveTemplatesRoot()
    {
        var searchRoots = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, TemplatesDirectoryName),
        };

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            searchRoots.Add(Path.Combine(current.FullName, TemplatesDirectoryName));
            current = current.Parent;
        }

        foreach (var candidate in searchRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, ManifestFileName)))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw Logger.Error($"Unable to locate '{TemplatesDirectoryName}/{ManifestFileName}' relative to '{AppContext.BaseDirectory}'.");
    }
}
