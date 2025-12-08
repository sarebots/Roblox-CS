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

public class StructFallbackTests
{
    private static readonly IReadOnlyList<MetadataReference> DefaultReferences =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
    ];

    [Fact]
    public void StructDeclaration_FallsBackToClassLowering_WhenUnsupported()
    {
        const string source = """
namespace Demo
{
    public unsafe struct Sample
    {
        public int* Pointer;
    }
}
""";

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = (CompilationUnitSyntax)tree.GetRoot();

        var compilation = CSharpCompilation.Create(
            "Sample",
            [tree],
            DefaultReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        var transpiler = new CSharpTranspiler(
            new TranspilerOptions(ScriptType.Module, new MacroOptions()),
            compilation,
            root);

        var rendered = new RendererWalker().Render(transpiler.Transpile());
        Assert.DoesNotContain("TODO: struct", rendered);
        Assert.StartsWith("local Sample", rendered.TrimStart());
        Assert.Contains("local function Sample", rendered);
    }

    [Fact]
    public void NestedStruct_FallsBackToClassLowering()
    {
        const string source = """
namespace Demo
{
    public class Container
    {
        public unsafe struct Nested
        {
            public int* Pointer;
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
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        var transpiler = new CSharpTranspiler(
            new TranspilerOptions(ScriptType.Module, new MacroOptions()),
            compilation,
            root);

        var rendered = new RendererWalker().Render(transpiler.Transpile());
        Assert.DoesNotContain("TODO: struct", rendered);
        Assert.Contains("Container_Nested", rendered);
        var predeclIndex = rendered.IndexOf("local Container_Nested", StringComparison.Ordinal);
        var ctorIndex = rendered.IndexOf("function Container_Nested", StringComparison.Ordinal);
        Assert.True(predeclIndex >= 0 && ctorIndex > predeclIndex, "Nested struct predeclaration should precede its constructor.");
    }

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
        Assert.Contains("local Sample", rendered);
        Assert.Contains("function Sample:Increment", rendered);
    }

    [Fact]
    public void StructWithExpressionBodiedMethod_FallsBackToClassLowering()
    {
        const string source = """
namespace Demo
{
    public struct Sample
    {
        public int Value;
        public int Increment() => Value + 2;
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
        Assert.Contains("return self.Value + 2", rendered);
    }

    [Fact]
    public void StructWithStaticMethod_FallsBackToClassLowering()
    {
        const string source = """
namespace Demo
{
    public struct Sample
    {
        public static int Add(int a, int b) => a + b;
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
        Assert.Contains("function Sample.Add", rendered);
        Assert.Contains("return a + b", rendered);
    }

    [Fact]
    public void StructWithStaticConstructor_FallsBackToClassLowering()
    {
        const string source = """
namespace Demo
{
    public struct Sample
    {
        public static int Value;
        static Sample() { Value = 10; }
        public int Increment() => Value + 1;
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
        Assert.Contains("Sample.Value = 10", rendered);
        Assert.Contains("function Sample:Increment", rendered);
    }

    [Fact]
    public void StructWithStaticConstructor_MultipleAssignmentsAreQualified()
    {
        const string source = """
namespace Demo
{
    public struct Sample
    {
        public static int A;
        public static int B;
        static Sample()
        {
            A = 1;
            B = A + 1;
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
        Assert.Contains("Sample.A = 1", rendered);
        Assert.Contains("Sample.B = Sample.A + 1", rendered);
    }

    [Fact]
    public void StructWithStaticProperty_AssignsDuringLowering()
    {
        const string source = """
namespace Demo
{
    public struct Sample
    {
        public static int Value { get; set; } = 5;
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
        Assert.Contains("Sample.Value = 5", rendered);
    }

    [Fact]
    public void StructWithStaticField_AssignsDuringLowering()
    {
        const string source = """
namespace Demo
{
    public struct Sample
    {
        public static int Value = 3;
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
        Assert.Contains("Sample.Value = 3", rendered);
    }

    [Fact]
    public void StructWithStaticEvent_AssignsSignalDuringLowering()
    {
        const string source = """
namespace Demo
{
    using System;

    public struct Sample
    {
        public static event Action Clicked;
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
        Assert.Contains("Sample.Clicked", rendered);
    }

    [Fact]
    public void StructWithStaticConstructor_AssignsStaticPropertyQualified()
    {
        const string source = """
namespace Demo
{
    public struct Sample
    {
        public static int Value { get; set; }
        static Sample()
        {
            Value = 10;
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
        Assert.Contains("Sample.Value = 10", rendered);
    }

    [Fact]
    public void StructWithStaticConstructor_AssignsStaticEventQualified()
    {
        const string source = """
namespace Demo
{
    using System;

    public struct Sample
    {
        public static event Action Clicked;
        static Sample()
        {
            Clicked = null;
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
        Assert.Contains("Sample.Clicked = nil", rendered);
        Assert.DoesNotContain("local Signal = require", rendered);
        Assert.DoesNotContain("Signal.new", rendered);
    }

    [Fact]
    public void StructWithStaticConstructor_AssignsStaticEventSignalOnce()
    {
        const string source = """
namespace Demo
{
    using System;

    public struct Sample
    {
        public static class Signal
        {
            public static Action New() => () => { };
        }

        public static event Action Clicked;
        static Sample()
        {
            Clicked = Signal.New();
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
        Assert.DoesNotContain("local Signal = require", rendered);
        Assert.Contains("Sample.Clicked = Demo.Sample_Signal.New()", rendered);
        Assert.Equal(1, rendered.Split("Sample.Clicked = Demo.Sample_Signal.New()", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void StructWithStaticConstructor_AssignsStaticEventCustomInitializer()
    {
        const string source = """
namespace Demo
{
    using System;

    public static class Helper
    {
        public static Action Create() => () => { };
    }

    public struct Sample
    {
        public static event Action Clicked;
        static Sample()
        {
            Clicked = Helper.Create();
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
        Assert.DoesNotContain("local Signal = require", rendered);
        Assert.DoesNotContain("Signal.new", rendered);
        Assert.Contains("Sample.Clicked = Demo.Helper.Create()", rendered);
    }

    [Fact]
    public void StructWithStaticConstructor_AssignsStaticEventExternalSignalStillImports()
    {
        const string source = """
namespace Demo
{
    using System;

    public static class Signal
    {
        public static Action New() => () => { };
    }

    public struct Sample
    {
        public static event Action Clicked;
        static Sample()
        {
            Clicked = Signal.New();
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
        Assert.DoesNotContain("local Signal = require", rendered);
        Assert.Contains("Sample.Clicked = Demo.Signal.New()", rendered);
    }

    [Fact]
    public void StructWithStaticConstructor_AssignsStaticEventAliasSignalSkipsImport()
    {
        const string source = """
namespace Helpers
{
    using System;

    public static class Signal
    {
        public static Action New() => () => { };
    }
}

namespace Demo
{
    using System;
    using Signal = Helpers.Signal;

    public struct Sample
    {
        public static event Action Clicked;
        static Sample()
        {
            Clicked = Signal.New();
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
        Assert.DoesNotContain("local Signal = require", rendered);
        Assert.Contains("Sample.Clicked = Helpers.Signal.New()", rendered);
    }

    [Fact]
    public void StructWithStaticConstructor_AssignsStaticEventAliasSignalSetToNilSkipsImport()
    {
        const string source = """
namespace Helpers
{
    using System;

    public static class Signal
    {
        public static Action New() => () => { };
    }
}

namespace Demo
{
    using System;
    using Signal = Helpers.Signal;

    public struct Sample
    {
        public static event Action Clicked;
        static Sample()
        {
            Clicked = null;
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
        Assert.DoesNotContain("local Signal = require", rendered);
        Assert.Contains("Sample.Clicked = nil", rendered);
    }

    [Fact]
    public void StructWithStaticConstructor_AssignsStaticEventQualifiedSignalSkipsImport()
    {
        const string source = """
namespace Helpers
{
    using System;

    public static class Signal
    {
        public static Action New() => () => { };
    }
}

namespace Demo
{
    using System;
    using Helpers;

    public struct Sample
    {
        public static event Action Clicked;
        static Sample()
        {
            Clicked = Signal.New();
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
        Assert.DoesNotContain("local Signal = require", rendered);
        Assert.Contains("Sample.Clicked = Helpers.Signal.New()", rendered);
    }
}
