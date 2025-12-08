using RobloxCS;
using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.Tests;

public class AnalyzerMacroTests
{
    public AnalyzerMacroTests() => Logger.Exit = false;

    [Fact]
    public void IteratorHelpersDisabled_FlagsIterInvocation()
    {
        const string source = """
                              using Roblox;

                              public static class IteratorHelperSample
                              {
                                  public static void Run()
                                  {
                                      foreach (var value in TS.iter(new[] { 1, 2 }))
                                      {
                                          _ = value;
                                      }
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3032]", exception.Message);
        Assert.Contains("TS.iter/TS.array_flatten are disabled", exception.Message);
    }

    [Fact]
    public void IteratorHelpersDisabled_FlagsArrayFlattenInvocation()
    {
        const string source = """
                              using Roblox;

                              public static class IteratorFlattenSample
                              {
                                  public static void Run()
                                  {
                                      foreach (var value in TS.array_flatten(new[] { new[] { 1 }, new[] { 2 } }))
                                      {
                                          _ = value;
                                      }
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3032]", exception.Message);
        Assert.Contains("TS.iter/TS.array_flatten are disabled", exception.Message);
    }

    [Fact]
    public void IteratorHelpers_ArgumentCountDiagnostic()
    {
        const string source = """
                              using Roblox;

                              public static class IteratorArgumentCount
                              {
                                  public static void Run()
                                  {
                                      foreach (var value in TS.iter(new[] { 1, 2 }, new[] { 3 }))
                                      {
                                          _ = value;
                                      }
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source, enableIteratorHelpers: true));
        Assert.Contains("[ROBLOXCS3033]", exception.Message);
    }

    [Fact]
    public void IteratorHelpers_SourceTypeDiagnostic()
    {
        const string source = """
                              using Roblox;

                              public static class IteratorSourceType
                              {
                                  public static void Run()
                                  {
                                      foreach (var value in TS.iter(123))
                                      {
                                          _ = value;
                                      }
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source, enableIteratorHelpers: true));
        Assert.Contains("[ROBLOXCS3034]", exception.Message);
    }

    [Fact]
    public void IteratorHelpers_ArrayFlatten_ArgumentCountDiagnostic()
    {
        const string source = """
                              using Roblox;

                              public static class IteratorArrayFlattenArgumentCount
                              {
                                  public static void Run()
                                  {
                                      foreach (var value in TS.array_flatten(new[] { new[] { 1 }, new[] { 2 } }, new[] { new[] { 3 } }))
                                      {
                                          _ = value;
                                      }
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source, enableIteratorHelpers: true));
        Assert.Contains("[ROBLOXCS3035]", exception.Message);
    }

    [Fact]
    public void IteratorHelpers_ArrayFlatten_SourceTypeDiagnostic()
    {
        const string source = """
                              using Roblox;

                              public static class IteratorArrayFlattenSourceType
                              {
                                  public static void Run()
                                  {
                                      foreach (var value in TS.array_flatten(new[] { 1, 2, 3 }))
                                      {
                                          _ = value;
                                      }
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source, enableIteratorHelpers: true));
        Assert.Contains("[ROBLOXCS3036]", exception.Message);
    }

    private static void Analyze(string source)
        => Analyze(source, enableIteratorHelpers: false);

    private static void Analyze(string source, bool enableIteratorHelpers)
    {
        var macroOptions = new MacroOptions
        {
            EnableIteratorHelpers = enableIteratorHelpers ? true : false
        };

        var config = ConfigReader.UnitTestingConfig with { Macro = macroOptions };

        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        var analyzer = new Analyzer(file, compiler);
        analyzer.Analyze(file.Tree.GetRoot());
    }
}
