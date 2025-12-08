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

public class InterfaceAliasTests
{
    private static readonly IReadOnlyList<MetadataReference> DefaultReferences =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
    ];

    [Fact]
    public void InterfaceWithStaticMembers_EmitsInstanceAndStaticAliases()
    {
        const string source = """
namespace Demo
{
    public interface ISample
    {
        int Value { get; set; }
        void Foo();
        static void Bar() { }
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

        Assert.Contains("type ISample", rendered);
        Assert.Contains("type ISample_static", rendered);
        Assert.DoesNotContain("TODO: interface", rendered);
    }

    [Fact]
    public void InterfaceWithoutStaticMembers_EmitsOnlyInstanceAlias()
    {
        const string source = """
namespace Demo
{
    public interface IOnlyInstance
    {
        int Value { get; set; }
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

        Assert.Contains("type IOnlyInstance", rendered);
        Assert.DoesNotContain("type IOnlyInstance_static", rendered);
        Assert.DoesNotContain("TODO: interface", rendered);
    }

    [Fact]
    public void NestedInterface_UsesScopedAliasName()
    {
        const string source = """
namespace Demo
{
    public class Container
    {
        public interface IInner
        {
            void Foo();
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

        Assert.Contains("type Container_IInner", rendered);
        Assert.DoesNotContain("TODO: interface", rendered);
    }

    [Fact]
    public void GenericInterface_EmitsGenericAlias()
    {
        const string source = """
namespace Demo
{
    public interface IFoo<T>
    {
        T Value { get; }
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

        Assert.Contains("type IFoo<T>", rendered);
        Assert.DoesNotContain("TODO: interface", rendered);
    }
}
