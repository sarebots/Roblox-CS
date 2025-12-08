using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using RobloxCS;
using RobloxCS.Luau;
using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.Tests;

public class AnalyzerIteratorTests
{
    public AnalyzerIteratorTests() => Logger.Exit = false;

    [Fact]
    public void AwaitForeachStatement_ThrowsRobloxCs3019()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading.Tasks;

                              public static class AwaitForeachStatement
                              {
                                  public static async Task Run(IAsyncEnumerable<int> values)
                                  {
                                      await foreach (var value in values)
                                      {
                                          _ = value;
                                      }
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3019]", exception.Message);
        Assert.Contains("await foreach loops are not supported", exception.Message);
    }

    [Fact]
    public void AwaitForeachVariableStatement_ThrowsRobloxCs3019()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading.Tasks;

                              public static class AwaitForeachVariableStatement
                              {
                                  public static async Task Run(IAsyncEnumerable<(int a, int b)> values)
                                  {
                                      await foreach (var (head, tail) in values)
                                      {
                                          _ = head + tail;
                                      }
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3019]", exception.Message);
        Assert.Contains("await foreach loops are not supported", exception.Message);
    }

    [Fact]
    public void AwaitForeachInsideLocalFunction_ThrowsRobloxCs3019()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading.Tasks;

                              public static class AwaitForeachLocalFunction
                              {
                                  public static async Task Run(IAsyncEnumerable<int> values)
                                  {
                                      async Task Iterate()
                                      {
                                          await foreach (var value in values)
                                          {
                                              _ = value;
                                          }
                                      }

                                      await Iterate();
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3019]", exception.Message);
        Assert.Contains("await foreach loops are not supported", exception.Message);
    }

    [Fact]
    public void AwaitForeachInsideLambda_ThrowsRobloxCs3019()
    {
        const string source = """
                              using System;
                              using System.Collections.Generic;
                              using System.Threading.Tasks;

                              public static class AwaitForeachLambda
                              {
                                  public static async Task Run(IAsyncEnumerable<int> values)
                                  {
                                      Func<Task> worker = async () =>
                                      {
                                          await foreach (var value in values)
                                          {
                                              _ = value;
                                          }
                                      };

                                      await worker();
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3019]", exception.Message);
        Assert.Contains("await foreach loops are not supported", exception.Message);
    }

    [Fact]
    public void AwaitForeachInsideAnonymousMethod_ThrowsRobloxCs3019()
    {
        const string source = """
                              using System;
                              using System.Collections.Generic;
                              using System.Threading.Tasks;

                              public static class AwaitForeachAnonymousMethod
                              {
                                  public static async Task Run(IAsyncEnumerable<int> values)
                                  {
                                      Func<Task> worker = async delegate
                                      {
                                          await foreach (var value in values)
                                          {
                                              _ = value;
                                          }
                                      };

                                      await worker();
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3019]", exception.Message);
        Assert.Contains("await foreach loops are not supported", exception.Message);
    }

    [Fact]
    public void AsyncIteratorYield_ThrowsRobloxCs3020()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Threading.Tasks;

                              public static class AsyncIteratorSample
                              {
                                  public static async IAsyncEnumerable<int> Run()
                                  {
                                      yield return 1;
                                      await Task.Delay(0);
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3020]", exception.Message);
        Assert.Contains("Async iterator methods (yield inside async functions) are not supported.", exception.Message);
    }

    [Fact]
    public void AsyncIteratorLocalFunction_ThrowsRobloxCs3020()
    {
        const string source = """
                              using System.Threading.Tasks;

                              public static class AsyncIteratorLocalFunction
                              {
                                  public static void Run()
                                  {
                                      async System.Collections.Generic.IAsyncEnumerable<int> Build()
                                      {
                                          yield return 1;
                                          await Task.Yield();
                                      }

                                      _ = Build();
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3020]", exception.Message);
        Assert.Contains("Async iterator methods (yield inside async functions) are not supported.", exception.Message);
    }

    [Fact]
    public void AsyncIteratorLambda_ThrowsRobloxCs3020()
    {
        const string source = """
                              using System;
                              using System.Collections.Generic;
                              using System.Threading.Tasks;

                              public static class AsyncIteratorLambda
                              {
                                  public static void Run()
                                  {
                                      Func<IAsyncEnumerable<int>> factory = async () =>
                                      {
                                          yield return 1;
                                          await Task.Yield();
                                      };

                                      _ = factory();
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3020]", exception.Message);
        Assert.Contains("Async iterator methods (yield inside async functions) are not supported.", exception.Message);
    }

    [Fact]
    public void AsyncIteratorAnonymousMethod_ThrowsRobloxCs3020()
    {
        const string source = """
                              using System;
                              using System.Collections.Generic;
                              using System.Threading.Tasks;

                              public static class AsyncIteratorAnonymousMethod
                              {
                                  public static void Run()
                                  {
                                      Func<IAsyncEnumerable<int>> factory = async delegate
                                      {
                                          yield return 1;
                                          await Task.Yield();
                                      };

                                      _ = factory();
                                  }
                              }
                              """;

        var exception = Assert.Throws<CleanExitException>(() => Analyze(source));
        Assert.Contains("[ROBLOXCS3020]", exception.Message);
        Assert.Contains("Async iterator methods (yield inside async functions) are not supported.", exception.Message);
    }

    private static void Analyze(string source)
    {
        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config, transformers: Array.Empty<Func<FileCompilation, SyntaxTree>>());
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        var analyzer = new Analyzer(file, compiler);
        analyzer.Analyze(file.Tree.GetRoot());
    }
}
