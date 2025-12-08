using System;
using RobloxCS.AST;
using RobloxCS.Luau;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2;
using Xunit;

namespace RobloxCS.Tests;

public class TryStatementTests
{
    [Fact]
    public void TryFinally_EmitsCsTryCall()
    {
        const string source = """
                             class Sample
                             {
                                 public void Run()
                                 {
                                     try
                                     {
                                         int x = 0;
                                     }
                                     finally
                                     {
                                         int y = 1;
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("CS.try(function()", rendered);
        Assert.Contains(", nil, function()", rendered);
    }

    [Fact]
    public void TryCatch_EmitsCsTryCallWithCatch()
    {
        const string source = """
                             class Sample
                             {
                                 public void Run()
                                 {
                                     try
                                     {
                                         int x = 0;
                                     }
                                     catch (System.Exception ex)
                                     {
                                         var message = ex.Message;
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("CS.try(function()", rendered);
        Assert.Contains(", function(ex", rendered);
    }

    [Fact]
    public void TryReturn_PropagatesReturnThroughCsTry()
    {
        const string source = """
                             class Sample
                             {
                                 public int Run()
                                 {
                                     try
                                     {
                                         return 5;
                                     }
                                     finally
                                     {
                                         int y = 1;
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("local __tryExitType, __tryReturns = CS.try", rendered);
        Assert.Contains("if __tryExitType == CS.TRY_RETURN", rendered);
        Assert.Contains("if __tryExitType ~= nil then", rendered);
    }

    [Fact]
    public void TryBreak_PropagatesBreakThroughCsTry()
    {
        const string source = """
                             class Sample
                             {
                                 public int Run()
                                 {
                                     int counter = 0;
                                     while (counter < 3)
                                     {
                                         try
                                         {
                                             break;
                                         }
                                         finally
                                         {
                                             counter = counter + 1;
                                         }
                                     }

                                     return counter;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("if __tryExitType == CS.TRY_BREAK", rendered);
        Assert.Contains("break", rendered);
        Assert.Contains("if __tryExitType ~= nil then", rendered);
    }

    [Fact]
    public void TryContinue_PropagatesContinueThroughCsTry()
    {
        const string source = """
                             class Sample
                             {
                                 public int Run()
                                 {
                                     int i = 0;
                                     int hits = 0;
                                     while (i < 3)
                                     {
                                         try
                                         {
                                             i = i + 1;
                                             continue;
                                         }
                                         finally
                                         {
                                             hits = hits + 1;
                                         }
                                     }

                                     return hits;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("if __tryExitType == CS.TRY_CONTINUE", rendered);
        Assert.Contains("continue", rendered);
        Assert.Contains("if __tryExitType ~= nil then", rendered);
    }

    [Fact]
    public void TryMultipleCatch_ThrowsNotSupported()
    {
        const string source = """
                             class Sample
                             {
                                 public void Run()
                                 {
                                     try
                                     {
                                         int value = 0;
                                     }
                                     catch (System.Exception ex)
                                     {
                                         var message = ex.Message;
                                     }
                                     catch (System.ArgumentException arg)
                                     {
                                         var message = arg.Message;
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Assert.Throws<NotSupportedException>(() => TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module));
    }

    [Fact]
    public void TryInFinally_EmitsNestedCsTry()
    {
        const string source = """
                             class Sample
                             {
                                 public void Run()
                                 {
                                     try
                                     {
                                         System.Console.WriteLine(0);
                                     }
                                     finally
                                     {
                                         try
                                         {
                                             System.Console.WriteLine(1);
                                         }
                                         finally
                                         {
                                             System.Console.WriteLine(2);
                                         }
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var outerIndex = rendered.IndexOf("CS.try", StringComparison.Ordinal);
        Assert.True(outerIndex >= 0, "Outer CS.try not found.");
        var innerIndex = rendered.IndexOf("CS.try", outerIndex + 1, StringComparison.Ordinal);
        Assert.True(innerIndex > outerIndex, "Nested CS.try not found inside finally block.");
    }

    [Fact]
    public void CatchlessFinallyWithLoop_EmitsWhile()
    {
        const string source = """
                             class Sample
                             {
                                 public int Run()
                                 {
                                     int value = 0;
                                     try
                                     {
                                         value = value + 1;
                                     }
                                     finally
                                     {
                                         while (value < 3)
                                         {
                                             value = value + 1;
                                         }
                                     }

                                     return value;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("while ", rendered);
        Assert.Contains("function()", rendered);
    }

    [Fact]
    public void TryInCatch_AllowsNestedTryFinally()
    {
        const string source = """
                             class Sample
                             {
                                 public void Run()
                                 {
                                     int value = 0;
                                     try
                                     {
                                         value = value + 1;
                                     }
                                     catch (System.Exception)
                                     {
                                         try
                                         {
                                             value = value + 2;
                                         }
                                         finally
                                         {
                                             value = value + 3;
                                         }
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var outer = rendered.IndexOf("CS.try", StringComparison.Ordinal);
        Assert.True(outer >= 0, "Outer CS.try not found.");
        var inner = rendered.IndexOf("CS.try", outer + 1, StringComparison.Ordinal);
        Assert.True(inner > outer, "Nested CS.try not found inside catch block.");
    }

    [Fact]
    public void NestedTryInCatch_EmitsNestedCsTry()
    {
        const string source = """
                             class Sample
                             {
                                 public int Run()
                                 {
                                     try
                                     {
                                         int value = 0;
                                     }
                                     catch (System.Exception)
                                     {
                                         try
                                         {
                                             return 5;
                                         }
                                         catch
                                         {
                                             return 6;
                                         }
                                     }
                                     return 0;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var first = rendered.IndexOf("CS.try", StringComparison.Ordinal);
        Assert.True(first >= 0, "Outer CS.try not found.");
        var second = rendered.IndexOf("CS.try", first + 1, StringComparison.Ordinal);
        Assert.True(second > first, "Nested CS.try not found inside catch block.");
    }

    [Fact]
    public void BreakInFinally_PropagatesBreak()
    {
        const string source = """
                             class Sample
                             {
                                 public void Run()
                                 {
                                     int counter = 0;
                                     while (counter < 1)
                                     {
                                         counter = counter + 1;
                                         try
                                         {
                                             int value = 0;
                                         }
                                         finally
                                         {
                                             break;
                                         }

                                         counter = counter + 1;
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("CS.TRY_BREAK", rendered);
        Assert.Contains("break", rendered);
    }

    [Fact]
    public void ContinueInFinally_PropagatesContinue()
    {
        const string source = """
                             class Sample
                             {
                                 public void Run()
                                 {
                                     int counter = 0;
                                     while (counter < 2)
                                     {
                                         counter = counter + 1;
                                         try
                                         {
                                             int value = 0;
                                         }
                                         finally
                                         {
                                             continue;
                                         }
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("CS.TRY_CONTINUE", rendered);
        Assert.Contains("continue", rendered);
    }
}
