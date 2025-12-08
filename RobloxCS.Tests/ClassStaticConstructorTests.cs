using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.Renderer;
using RobloxCS.TranspilerV2;
using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.Tests;

public class ClassStaticConstructorTests
{
    private static readonly IReadOnlyList<MetadataReference> DefaultReferences =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
    ];

    [Fact]
    public void ClassStaticConstructor_QualifiesStaticPropertyAssignments()
    {
        const string source = """
namespace Demo
{
    public class Sample
    {
        public static int Value { get; set; }
        static Sample()
        {
            Value = 42;
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
        Assert.Contains("Sample.Value = 42", rendered);
    }

    [Fact]
    public void ClassStaticConstructor_QualifiesStaticFieldAssignments()
    {
        const string source = """
namespace Demo
{
    public class Sample
    {
        public static int Value;
        static Sample()
        {
            Value = 7;
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
        Assert.Contains("Sample.Value = 7", rendered);
    }

    [Fact]
    public void ClassStaticConstructor_QualifiesStaticEventAssignments()
    {
        const string source = """
namespace Demo
{
    using System;

    public class Sample
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
        Assert.Contains("Sample.Clicked = nil", rendered);
        Assert.DoesNotContain("Signal.new", rendered);
    }

    [Fact]
    public void ClassStaticConstructor_DoesNotImportSignalForNilEventAssignment()
    {
        const string source = """
namespace Demo
{
    using System;

    public class Sample
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
        Assert.DoesNotContain("local Signal = require", rendered);
        Assert.Contains("Sample.Clicked = nil", rendered);
    }

    [Fact]
    public void ClassStaticConstructor_ImportsSignalWhenCtorUsesExternalHelper()
    {
        const string source = """
namespace Demo
{
    using System;

    public static class Signal
    {
        public static Action New() => () => { };
    }

    public class Sample
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
        Assert.DoesNotContain("local Signal = require", rendered);
        Assert.Contains("Sample.Clicked = Demo.Signal.New()", rendered);
    }

    [Fact]
    public void ClassStaticConstructor_SkipsSignalImportForNestedHelper()
    {
        const string source = """
namespace Demo
{
    using System;

    public class Sample
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
        Assert.DoesNotContain("local Signal = require", rendered);
        Assert.Contains("Sample.Clicked = Demo.Sample_Signal.New()", rendered);
    }

    [Fact]
    public void ClassStaticConstructor_SkipsSignalImportForAliasHelper()
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

    public class Sample
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
        Assert.DoesNotContain("local Signal = require", rendered);
        Assert.Contains("Sample.Clicked = Helpers.Signal.New()", rendered);
    }

    [Fact]
    public void ClassStaticConstructor_SkipsSignalImportForAliasHelperWhenAssigningNil()
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

    public class Sample
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
        Assert.DoesNotContain("local Signal = require", rendered);
        Assert.Contains("Sample.Clicked = nil", rendered);
    }

    [Fact]
    public void ClassStaticConstructor_SkipsSignalImportForQualifiedHelper()
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

    public class Sample
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
        Assert.DoesNotContain("local Signal = require", rendered);
        Assert.Contains("Sample.Clicked = Helpers.Signal.New()", rendered);
    }
}
