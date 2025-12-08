using RobloxCS.Luau;
using RobloxCS.Shared;

namespace RobloxCS.Tests;

public class WholeFileRenderingTest
{
    [Fact]
    public void Renders_BasicFunctionsAndVariables()
    {
        const string source = """
                              var n = 42;
                              var doubled = DoSomethingElse(n);
                              var mainDoubled = DoSomething();
                              
                              int DoSomething() => DoSomethingElse(69);
                              int DoSomethingElse(int x)
                              {
                                print("x:", x);
                                return x * 2;
                              }
                              """;
        
        const string expectedOutput = """
                                      local function DoSomethingElse(x: number): number
                                        print("x:", x)
                                        return x * 2
                                      end
                                      local function DoSomething(): number
                                        return DoSomethingElse(69)
                                      end
                                      local n = 42
                                      local doubled = DoSomethingElse(n)
                                      local mainDoubled = DoSomething()
                                      return nil
                                      """;

        var output = Emit(source);
        Assert.Equal(expectedOutput.Replace("\r", ""), string.Join('\n', output.Replace("\r", "").Split('\n').Skip(1)).Trim());
    }

    [Fact]
    public void Renders_AsyncInstanceMethodWrapsWithCsAsync()
    {
        const string source = """
                              using System.Threading.Tasks;

                              class Sample
                              {
                                  public async Task<int> Foo(int value)
                                  {
                                      await Task.FromResult(value);
                                      return value + 1;
                                  }
                              }
                              """;

        var output = Emit(source);
        Assert.Contains("Sample.Foo = CS.async", output);
        Assert.Contains("function(self: Sample, value: number", output);
        Assert.Contains("CS.await(", output);
    }

    [Fact]
    public void Renders_AsyncLocalFunctionWrapsWithCsAsync()
    {
        const string source = """
                              using System.Threading.Tasks;

                              class Sample
                              {
                                  public void Run()
                                  {
                                      async Task<int> Local(int value)
                                      {
                                          await Task.FromResult(value);
                                          return value * 2;
                                      }
                                  }
                              }
                              """;

        var output = Emit(source);
        Assert.Contains("local Local = CS.async", output);
        Assert.Contains("CS.await(", output);
    }

    [Fact]
    public void Renders_AsyncLambdaWrapsWithCsAsync()
    {
        const string source = """
                              using System;
                              using System.Threading.Tasks;

                              class Sample
                              {
                                  public void Run()
                                  {
                                      Func<int, Task<int>> handle = async value =>
                                      {
                                          await Task.FromResult(value);
                                          return value + 3;
                                      };
                                  }
                              }
                              """;

        var output = Emit(source);
        Assert.Contains("local handle: (number) -> Task<number> = CS.async", output);
        Assert.Contains("CS.async(function(value): ()", output);
        Assert.Contains("CS.await(", output);
    }
    
    private static string Emit(string source)
    {
      var config = ConfigReader.UnitTestingConfig;
      var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
      var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
      var ast = TranspilerUtility.GetLuauAST(file, compiler);
      var writer = new LuauWriter();
      ast.Render(writer);

      return writer.ToString();
    }
}
