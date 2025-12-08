using System;
using RobloxCS;
using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.Tests;

public class AnalyzerStructDiagnosticsTests
{
    public AnalyzerStructDiagnosticsTests() => Logger.Exit = false;

    [Fact]
    public void RecordDeclarationsAreBlocked()
    {
        const string source = """
                              public record Person(string Name);
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3041]", exception.Message);
        Assert.Contains("Record declarations are not supported yet", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefStructDeclarationsAreBlocked()
    {
        const string source = """
                              using System;

                              public ref struct Buffer
                              {
                                  public Span<int> Data;
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3042]", exception.Message);
        Assert.Contains("ref struct declarations are not supported yet", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefLikeStructFieldsAreBlocked()
    {
        const string source = """
                              using System;

                              public struct Container
                              {
                                  public Span<int> Buffer;
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3044]", exception.Message);
        Assert.Contains("fields, properties, or indexers of ref-like types", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefLikeStructPropertiesAreBlocked()
    {
        const string source = """
                              using System;

                              public struct Holder
                              {
                                  public Span<int> Data { get; set; }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3044]", exception.Message);
        Assert.Contains("fields, properties, or indexers of ref-like types", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefLikeStructIndexersAreBlocked()
    {
        const string source = """
                              using System;

                              public struct Buffer
                              {
                                  public Span<int> this[int index]
                                  {
                                      get => Span<int>.Empty;
                                      set { }
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3044]", exception.Message);
        Assert.Contains("fields, properties, or indexers of ref-like types", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstanceStructMethodsAreBlocked()
    {
        const string source = """
                              public struct Worker
                              {
                                  public void Run() { }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3043]", exception.Message);
        Assert.Contains("Struct methods are not supported yet", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void Analyze(string source)
    {
        var trimmed = source.Trim();
        var file = TranspilerUtility.ParseAndTransformTree(trimmed, new RojoProject(), ConfigReader.UnitTestingConfig);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], ConfigReader.UnitTestingConfig);
        var analyzer = new Analyzer(file, compiler);
        analyzer.Analyze(file.Tree.GetRoot());
    }
}
