using RobloxCS;
using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.Tests;

public class AnalyzerPromiseTests
{
    public AnalyzerPromiseTests() => Logger.Exit = false;

    [Fact]
    public void AnalyzerReportsPromiseFromEventPredicateViaQualifiedAccess()
    {
        const string source = """
                              using Roblox;

                              class PromisePredicateSample
                              {
                                  public Roblox.Promise<string> Watch(object signal)
                                  {
                                      return Promise.FromEvent<string>(signal, value => value);
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3016]", exception.Message);
        Assert.Contains("Promise.FromEvent predicates must return a boolean expression.", exception.Message);
    }

    [Fact]
    public void AnalyzerReportsPromiseFromEventPredicateViaStaticImport()
    {
        const string source = """
                              using Roblox;
                              using static Roblox.Promise;

                              class PromisePredicateSample
                              {
                                  public Promise<string> Watch(object signal)
                                  {
                                      return FromEvent<string>(signal, value => value);
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3016]", exception.Message);
        Assert.Contains("Promise.FromEvent predicates must return a boolean expression.", exception.Message);
    }

    private static void Analyze(string source)
    {
        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        var analyzer = new Analyzer(file, compiler);
        analyzer.Analyze(file.Tree.GetRoot());
    }
}
