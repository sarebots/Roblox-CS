using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Functions;
using RobloxCS.AST.Generics;
using RobloxCS.AST.Parameters;
using RobloxCS.AST.Prefixes;
using RobloxCS.AST.Statements;
using RobloxCS.AST.Suffixes;
using RobloxCS.AST.Types;
using RobloxCS.Renderer;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2;
using RobloxCS.TranspilerV2.Builders;
using AstTypeInfo = RobloxCS.AST.Types.TypeInfo;

namespace RobloxCS.Tests;

public class TypeInfoTests
{
    private static readonly IReadOnlyList<MetadataReference> DefaultReferences = GetDefaultReferences();

    [Fact]
    public void OptionalTypeInfo_DeepCloneClonesInner()
    {
        var optional = new OptionalTypeInfo { Inner = BasicTypeInfo.String() };

        var clone = (OptionalTypeInfo)optional.DeepClone();

        Assert.NotSame(optional, clone);
        Assert.IsType<BasicTypeInfo>(clone.Inner);
        Assert.NotSame(optional.Inner, clone.Inner);

        var child = Assert.Single(optional.Children());
        Assert.Same(optional.Inner, child);
    }

    [Fact]
    public void TupleTypeInfo_DeepCloneMaintainsElementsAndVariadicTail()
    {
        var tuple = new TupleTypeInfo
        {
            Elements = new List<AstTypeInfo>
            {
                BasicTypeInfo.Number(),
                new OptionalTypeInfo { Inner = BasicTypeInfo.String() },
            },
            VariadicTail = new VariadicTypeInfo { Inner = BasicTypeInfo.Boolean() },
        };

        var clone = (TupleTypeInfo)tuple.DeepClone();

        Assert.NotSame(tuple, clone);
        Assert.Equal(tuple.Elements.Count, clone.Elements.Count);
        for (var i = 0; i < tuple.Elements.Count; i++)
        {
            Assert.NotSame(tuple.Elements[i], clone.Elements[i]);
        }

        Assert.NotNull(clone.VariadicTail);
        Assert.NotSame(tuple.VariadicTail, clone.VariadicTail);

        var children = tuple.Children().ToList();
        Assert.Equal(3, children.Count);
        Assert.Same(tuple.Elements[0], children[0]);
        Assert.Same(tuple.Elements[1], children[1]);
        Assert.Same(tuple.VariadicTail, children[2]);
    }

    [Fact]
    public void VariadicTypeInfo_DeepCloneClonesInner()
    {
        var variadic = new VariadicTypeInfo { Inner = BasicTypeInfo.String() };

        var clone = (VariadicTypeInfo)variadic.DeepClone();

        Assert.NotSame(variadic, clone);
        Assert.IsType<BasicTypeInfo>(clone.Inner);
        Assert.NotSame(variadic.Inner, clone.Inner);

        var child = Assert.Single(variadic.Children());
        Assert.Same(variadic.Inner, child);
    }

    [Fact]
    public void TypeOfTypeInfo_DeepCloneClonesExpression()
    {
        var expr = SymbolExpression.FromString("Foo");
        var typeOf = new TypeOfTypeInfo { Expression = expr };

        var clone = (TypeOfTypeInfo)typeOf.DeepClone();

        Assert.NotSame(typeOf, clone);
        Assert.NotSame(typeOf.Expression, clone.Expression);

        var child = Assert.Single(typeOf.Children());
        Assert.Same(expr, child);
    }

    [Fact]
    public void FunctionBody_DeepClonePreservesNestedNodes()
    {
        var generic = new GenericDeclaration
        {
            Parameters =
            [
                new GenericDeclarationParameter
                {
                    Parameter = NameGenericParameter.FromString("T"),
                    Default = new OptionalTypeInfo { Inner = BasicTypeInfo.String() },
                },
            ],
        };

        var body = new FunctionBody
        {
            Generics = [generic],
            Parameters = [NameParameter.FromString("self")],
            TypeSpecifiers = [BasicTypeInfo.FromString("T")],
            ReturnType = new VariadicTypeInfo
            {
                Inner = new TupleTypeInfo
                {
                    Elements = [BasicTypeInfo.Number()],
                },
            },
            Body = Block.Empty(),
        };

        var clone = (FunctionBody)body.DeepClone();

        Assert.NotSame(body, clone);
        Assert.NotSame(body.Generics![0], clone.Generics![0]);
        Assert.NotSame(body.Parameters[0], clone.Parameters[0]);
        Assert.NotSame(body.TypeSpecifiers[0], clone.TypeSpecifiers[0]);
        Assert.NotSame(body.ReturnType, clone.ReturnType);
        Assert.NotSame(body.Body, clone.Body);

        var children = body.Children().ToList();
        Assert.Equal(5, children.Count);
        Assert.Same(body.Generics![0], children[0]);
        Assert.Same(body.Parameters[0], children[1]);
        Assert.Same(body.TypeSpecifiers[0], children[2]);
        Assert.Same(body.ReturnType, children[3]);
        Assert.Same(body.Body, children[4]);
    }

    [Fact]
    public void TypeInfoFromSymbol_ProducesOptionalTupleAndNullableNodes()
    {
        const string source = """
#nullable enable
using System;

class Sample
{
    private string? optionalField;
    private (string, int) tupleField;
    private int? nullableNumber;
}
""";

        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "Sample",
            [tree],
            DefaultReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var declarators = root.DescendantNodes().OfType<VariableDeclaratorSyntax>().ToArray();

        static IFieldSymbol ResolveFieldSymbol(SemanticModel semanticModel, VariableDeclaratorSyntax variable) =>
            (IFieldSymbol)semanticModel.GetDeclaredSymbol(variable)!;

        var optionalSymbol = ResolveFieldSymbol(model, declarators[0]).Type;
        var optionalInfo = SyntaxUtilities.TypeInfoFromSymbol(optionalSymbol);
        var optional = Assert.IsType<OptionalTypeInfo>(optionalInfo);
        Assert.Equal("string", Assert.IsType<BasicTypeInfo>(optional.Inner).Name);

        var tupleSymbol = ResolveFieldSymbol(model, declarators[1]).Type;
        var tupleInfo = SyntaxUtilities.TypeInfoFromSymbol(tupleSymbol);
        var tuple = Assert.IsType<TupleTypeInfo>(tupleInfo);
        Assert.Collection(
            tuple.Elements,
            first => Assert.Equal("string", Assert.IsType<BasicTypeInfo>(first).Name),
            second => Assert.Equal("number", Assert.IsType<BasicTypeInfo>(second).Name));

        var nullableNumberSymbol = ResolveFieldSymbol(model, declarators[2]).Type;
        var nullableNumberInfo = SyntaxUtilities.TypeInfoFromSymbol(nullableNumberSymbol);
        var nullable = Assert.IsType<OptionalTypeInfo>(nullableNumberInfo);
        Assert.Equal("number", Assert.IsType<BasicTypeInfo>(nullable.Inner).Name);
    }

    [Fact]
    public void RendererWalker_RendersTupleTypeWithVariadicTail()
    {
        var tupleType = new TupleTypeInfo
        {
            Elements =
            [
                BasicTypeInfo.String(),
                new OptionalTypeInfo { Inner = BasicTypeInfo.Number() },
            ],
            VariadicTail = new VariadicTypeInfo { Inner = BasicTypeInfo.Boolean() },
        };

        var block = Block.Empty();
        block.AddStatement(new TypeDeclaration
        {
            Name = "TupleExample",
            DeclareAs = tupleType,
        });

        var chunk = new Chunk { Block = block };
        var output = new RendererWalker().Render(chunk);

        Assert.Equal("type TupleExample = string, number?, ...boolean\n", output);
    }

    [Fact]
    public void RendererWalker_RendersTypeOfTypeInfo()
    {
        var block = Block.Empty();
        block.AddStatement(new TypeDeclaration
        {
            Name = "FooType",
            DeclareAs = new TypeOfTypeInfo
            {
                Expression = SymbolExpression.FromString("Foo"),
            },
        });

        var chunk = new Chunk { Block = block };
        var output = new RendererWalker().Render(chunk);

        Assert.Equal("type FooType = typeof(Foo)\n", output);
    }

    [Fact]
    public void TypeInfoFromSymbol_HandlesNestedGenericOptionals()
    {
        const string source = """
#nullable enable
using System.Collections.Generic;

class Container
{
    private List<(string?, int?)?>? data;
}
""";

        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "Container",
            [tree],
            DefaultReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var model = compilation.GetSemanticModel(tree);
        var field = tree.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>().Single();
        var symbol = (IFieldSymbol)model.GetDeclaredSymbol(field.Declaration.Variables.Single())!;

        var typeInfo = SyntaxUtilities.TypeInfoFromSymbol(symbol.Type);
        var outerOptional = Assert.IsType<OptionalTypeInfo>(typeInfo);
        var listType = Assert.IsType<BasicTypeInfo>(outerOptional.Inner);
        Assert.Equal("List", listType.Name);
    }

    [Fact]
    public void RendererWalker_RendersOptionalTupleCombination()
    {
        var tuple = new TupleTypeInfo
        {
            Elements =
            [
                new OptionalTypeInfo { Inner = BasicTypeInfo.String() },
                BasicTypeInfo.Number(),
            ],
            VariadicTail = new VariadicTypeInfo { Inner = BasicTypeInfo.Boolean() },
        };

        var alias = new TypeDeclaration
        {
            Name = "Complex",
            DeclareAs = new OptionalTypeInfo { Inner = tuple },
        };

        var chunk = new Chunk { Block = Block.Empty() };
        chunk.Block.AddStatement(alias);

        var rendered = new RendererWalker().Render(chunk);
        Assert.Equal("type Complex = (string?, number, ...boolean)?\n", rendered);
    }

    [Fact]
    public void CreateStaticMethod_ParamsArrayAnnotatesVariadicOptional()
    {
        const string source = """
#nullable enable

class Sample
{
    public static void Log(params string?[] labels) { }
}
""";

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = (CompilationUnitSyntax)tree.GetRoot();

        var compilation = CSharpCompilation.Create(
            "Sample",
            [tree],
            DefaultReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var model = compilation.GetSemanticModel(tree);
        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var methodDecl = classDecl.Members.OfType<MethodDeclarationSyntax>().Single();

        var classSymbol = (INamedTypeSymbol)model.GetDeclaredSymbol(classDecl)!;
        var methodSymbol = (IMethodSymbol)model.GetDeclaredSymbol(methodDecl)!;

        var ctx = new TranspilationContext(new TranspilerOptions(ScriptType.Module, new MacroOptions()), compilation, root, model);

        var className = ctx.GetTypeName(classSymbol);
        var statement = FunctionBuilder.CreateStaticMethod(classSymbol, methodSymbol, methodDecl, ctx, className);
        var function = Assert.IsType<FunctionDeclaration>(statement);

        var typeSpecifier = Assert.Single(function.Body.TypeSpecifiers);
        var variadic = Assert.IsType<VariadicTypeInfo>(typeSpecifier);
        var optional = Assert.IsType<OptionalTypeInfo>(variadic.Inner);
        Assert.Equal("string", Assert.IsType<BasicTypeInfo>(optional.Inner).Name);
    }

    [Fact]
    public void CreateInstanceMethod_AsyncAnnotatesSelfType()
    {
        const string source = """
#nullable enable
using System.Threading.Tasks;

class Sample
{
    public async Task Foo(string? message)
    {
        await Task.CompletedTask;
    }
}
""";

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = (CompilationUnitSyntax)tree.GetRoot();

        var compilation = CSharpCompilation.Create(
            "Sample",
            [tree],
            DefaultReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var model = compilation.GetSemanticModel(tree);
        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var methodDecl = classDecl.Members.OfType<MethodDeclarationSyntax>().Single();

        var classSymbol = (INamedTypeSymbol)model.GetDeclaredSymbol(classDecl)!;
        var methodSymbol = (IMethodSymbol)model.GetDeclaredSymbol(methodDecl)!;

        var ctx = new TranspilationContext(new TranspilerOptions(ScriptType.Module, new MacroOptions()), compilation, root, model);
        ctx.MarkAsync(methodSymbol);

        var className = ctx.GetTypeName(classSymbol);
        var statement = FunctionBuilder.CreateInstanceMethod(classSymbol, methodSymbol, methodDecl, ctx, className);
        var assignment = Assert.IsType<Assignment>(statement);
        var asyncCall = Assert.IsType<FunctionCall>(Assert.Single(assignment.Expressions));
        var prefix = Assert.IsType<NamePrefix>(asyncCall.Prefix);
        Assert.Equal("CS.async", prefix.Name);

        var call = Assert.IsType<AnonymousCall>(Assert.Single(asyncCall.Suffixes));
        var asyncFunction = Assert.IsType<AnonymousFunction>(Assert.Single(call.Arguments.Arguments));
        var body = asyncFunction.Body;

        Assert.Equal(2, body.Parameters.Count);
        Assert.Equal("self", Assert.IsType<NameParameter>(body.Parameters[0]).Name);
        Assert.Equal("message", Assert.IsType<NameParameter>(body.Parameters[1]).Name);

        Assert.Equal(2, body.TypeSpecifiers.Count);
        Assert.Equal("Sample", Assert.IsType<BasicTypeInfo>(body.TypeSpecifiers[0]).Name);
        var optional = Assert.IsType<OptionalTypeInfo>(body.TypeSpecifiers[1]);
        Assert.Equal("string", Assert.IsType<BasicTypeInfo>(optional.Inner).Name);
    }

    [Fact]
    public void LocalDeclaration_UsesSemanticTypeInfo()
    {
        const string source = """
using System.Collections.Generic;

class Sample
{
    void Method()
    {
        string? maybe;
        (string? label, int count) tuple;
        var list = new List<string?>();
    }
}
""";

        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "Sample",
            [tree],
            DefaultReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var semanticModel = compilation.GetSemanticModel(tree);
        var root = (CompilationUnitSyntax)tree.GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var locals = method.Body!.Statements.OfType<LocalDeclarationStatementSyntax>().ToArray();

        var ctx = new TranspilationContext(new TranspilerOptions(ScriptType.Module, new MacroOptions()), compilation, root, semanticModel);

        var optionalAssignment = (LocalAssignment)StatementBuilder.Transpile(locals[0], ctx);
        var optionalType = Assert.IsType<OptionalTypeInfo>(optionalAssignment.Types[0]);
        Assert.Equal("string", Assert.IsType<BasicTypeInfo>(optionalType.Inner).Name);

        var tupleAssignment = (LocalAssignment)StatementBuilder.Transpile(locals[1], ctx);
        var tupleType = Assert.IsType<TupleTypeInfo>(tupleAssignment.Types[0]);
        Assert.IsType<OptionalTypeInfo>(tupleType.Elements[0]);
        Assert.Equal("number", Assert.IsType<BasicTypeInfo>(tupleType.Elements[1]).Name);

        var listAssignment = (LocalAssignment)StatementBuilder.Transpile(locals[2], ctx);
        Assert.Empty(listAssignment.Types);
    }

    private static IReadOnlyList<MetadataReference> GetDefaultReferences()
    {
        return
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ValueTuple).Assembly.Location),
        ];
    }
}
