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

public class StructInstanceMemberTests
{
    private static readonly IReadOnlyList<MetadataReference> DefaultReferences =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
    ];

    [Fact]
    public void StructWithInstanceMethod_FallsBackToClassLowering()
    {
        const string source = """
namespace Demo
{
    public struct Sample
    {
        public int Value;
        public int Increment() { return Value + 1; }
    }
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
        Assert.DoesNotContain("TODO: struct", rendered);
        Assert.Contains("function Sample:Increment", rendered);
    }

    [Fact]
    public void StructWithInstanceEvent_FallsBackToClassLowering()
    {
        const string source = """
namespace Demo
{
    using System;

    public struct Sample
    {
        public event Action Clicked;
    }
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
        Assert.DoesNotContain("TODO: struct", rendered);
        Assert.Contains("local Signal = require", rendered);
        Assert.Contains("Signal.new", rendered);
        Assert.Contains("local Sample", rendered);
        Assert.Contains("Clicked", rendered);

        var signalIndex = rendered.IndexOf("local Signal = require", StringComparison.Ordinal);
        var structIndex = rendered.IndexOf("local Sample", StringComparison.Ordinal);
        Assert.True(signalIndex >= 0 && structIndex > signalIndex, "Signal import should precede struct declaration.");
    }

    [Fact]
    public void StructWithAccessorProperty_FallsBackToClassLowering()
    {
        const string source = """
namespace Demo
{
    public struct Sample
    {
        private int _value;
        public int Value
        {
            get { return _value; }
            set { _value = value; }
        }
    }
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
        Assert.DoesNotContain("TODO: struct", rendered);
        Assert.Contains("local Sample", rendered);
        Assert.Contains("Value", rendered);
    }
}
