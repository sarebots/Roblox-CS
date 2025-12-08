using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.Renderer;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2;
using Xunit;

namespace RobloxCS.Tests;

public class RecordPlaceholderTests
{
    private static readonly IReadOnlyList<MetadataReference> DefaultReferences =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
    ];

    [Fact]
    public void RecordDeclaration_EmitsClassLikeOutput()
    {
        const string source = """
namespace Demo
{
    public record Sample(int Id);
}
""";

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = (CompilationUnitSyntax)tree.GetRoot();

        var compilation = CSharpCompilation.Create(
            "Sample",
            [tree],
            DefaultReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var transpiler = new CSharpTranspiler(
            new TranspilerOptions(ScriptType.Module, new MacroOptions()),
            compilation,
            root);

        var rendered = new RendererWalker().Render(transpiler.Transpile());
        Assert.DoesNotContain("TODO: record", rendered);
        Assert.Contains("local Sample", rendered);
        Assert.Contains("function Sample.new", rendered);
    }

    [Fact]
    public void RecordDeclaration_WithoutParameters_StillEmitsClass()
    {
        const string source = """
namespace Demo
{
    public record Empty;
}
""";

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = (CompilationUnitSyntax)tree.GetRoot();

        var compilation = CSharpCompilation.Create(
            "Sample",
            [tree],
            DefaultReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var transpiler = new CSharpTranspiler(
            new TranspilerOptions(ScriptType.Module, new MacroOptions()),
            compilation,
            root);

        var rendered = new RendererWalker().Render(transpiler.Transpile());
        Assert.DoesNotContain("TODO: record", rendered);
        Assert.Contains("local Empty", rendered);
        Assert.Contains("function Empty.new", rendered);
    }
}
