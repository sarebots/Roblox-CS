using RobloxCS;
using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.Tests;

public class AnalyzerPatternTests
{
    public AnalyzerPatternTests() => Logger.Exit = false;

    [Fact]
    public void AnalyzerReportsDuplicateBindingsWithRobloxcs2010()
    {
        const string source = """
                              using System.Collections.Generic;

                              class DuplicatePatternSample
                              {
                                  public string Describe(List<int> values) =>
                                      values switch
                                      {
                                          [var head, var head, .. var _] => "bad",
                                          _ => "ok",
                                      };
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS2010]", exception.Message);
        Assert.Contains("Variable 'head' is bound multiple times", exception.Message);
    }

    [Fact]
    public void AnalyzerReportsDiscardMisuseWithRobloxcs2012()
    {
        const string source = """
                              using System.Collections.Generic;

                              class DiscardSliceSample
                              {
                                  public string Describe(List<int> values) =>
                                      values switch
                                      {
                                          [_, .. var rest, _] => $"rest:{rest.Count}",
                                          _ => "ok",
                                      };
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS2012]", exception.Message);
        Assert.Contains("Discard patterns cannot surround slice captures", exception.Message);
    }

    [Fact]
    public void AnalyzerReportsGuardRestrictionWithRobloxcs2013()
    {
        const string source = """
                              using System.Collections.Generic;

                              class GuardSliceSample
                              {
                                  public int Describe(List<int> values) =>
                                      values switch
                                      {
                                          [var head, .. var rest] when rest.Count > head => rest.Count,
                                          _ => 0,
                                      };
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS2013]", exception.Message);
        Assert.Contains("Guard expressions cannot reference slice counts", exception.Message);
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
