using RobloxCS;
using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.Tests;

public class UnityAliasTransformerTests
{
    [Fact]
    public void UnityAliasTransformer_RewritesGameObjectCreation()
    {
        const string source = """
                              using UnityEngine;

                              public class GameObjectFactory
                              {
                                  public void Build()
                                  {
                                      var obj = new GameObject("Demo");
                                  }
                              }
                              """;

        var config = ConfigReader.UnitTestingConfig with { EnableUnityAliases = true };
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        var luau = TranspilerUtility.GenerateLuau(file, compiler);

        Assert.Contains("Roblox.UnityAliases.CreateGameObject", luau);
    }

    [Fact]
    public void UnityAliasTransformer_RewritesDebugLogCalls()
    {
        const string source = """
                              using UnityEngine;

                              public class Logger
                              {
                                  public void Run()
                                  {
                                      Debug.Log("Hello from Unity");
                                  }
                              }
                              """;

        var config = ConfigReader.UnitTestingConfig with { EnableUnityAliases = true };
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        var luau = TranspilerUtility.GenerateLuau(file, compiler);

        Assert.Contains("Roblox.UnityAliases.Log", luau);
    }
}
