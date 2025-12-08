using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RobloxCS.Shared;

public static class ConfigReader
{
    public static ConfigData UnitTestingConfig { get; } = new()
    {
        SourceFolder = "test-src",
        OutputFolder = "test-dist",
        RojoProjectName = "UNIT_TESTING",
        EntryPointArguments = [],
        ProjectRoot = Directory.GetCurrentDirectory(),
        Plugins = [],
        EnableUnityAliases = false,
        Macro = new MacroOptions()
    };

    private const string _fileName = "roblox-cs.yml";

    public static ConfigData Read(string inputDirectory)
    {
        var configPath = inputDirectory + "/" + _fileName;
        ConfigData? config = null;
        var ymlContent = "";

        try
        {
            ymlContent = File.ReadAllText(configPath);
        }
        catch (FileNotFoundException)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath) ?? inputDirectory);
            File.WriteAllText(configPath,
                "# roblox-cs project configuration\n" +
                "# Update these paths to match your project layout.\n" +
                "SourceFolder: src\n" +
                "OutputFolder: out\n" +
                "RojoProjectName: default\n" +
                "EntryPointArguments: []\n" +
                "RojoServePort: 34872\n" +
                "Plugins: []\n" +
                "EnableUnityAliases: false\n" +
                "Macro:\n" +
                "  EnableIteratorHelpers: true\n" +
                "  EnableMathMacros: true\n" +
                "  EnableBit32Macros: true\n");

            FailToRead($"Could not find {_fileName} at '{configPath}'. A starter file has been created; fill in the correct folders and rerun.");
        }
        catch (Exception e)
        {
            FailToRead(e.Message);
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .WithAttemptingUnquotedStringTypeDeserialization()
            .WithDuplicateKeyChecking()
            .Build();

        try
        {
            config = deserializer.Deserialize<ConfigData>(ymlContent);
        }
        catch (Exception e)
        {
            FailToRead(e.ToString());
        }

        if (config == null)
            FailToRead($"Invalid {_fileName}! Make sure it has all required fields.");

        config ??= ConfigReader.UnitTestingConfig;

        var normalizedPlugins = config.Plugins is { Count: > 0 } plugins
            ? new List<TransformerPluginConfig>(plugins)
            : new List<TransformerPluginConfig>();

        if (config.RojoServePort <= 0)
        {
            config = config with { RojoServePort = ConfigData.DefaultRojoPort };
        }

        config = config with
        {
            ProjectRoot = inputDirectory,
            Plugins = normalizedPlugins,
            Macro = config.Macro ?? new MacroOptions()
        };

        if (!config.IsValid())
            FailToRead($"Invalid {_fileName}! Make sure it has all required fields.");

        return config;
    }

    private static void FailToRead(string message) =>
        throw Logger.Error($"Failed to read {_fileName}!\n{message}");
}
