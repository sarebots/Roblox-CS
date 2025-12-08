using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.TranspilerV2;
using Xunit;

namespace RobloxCS.Tests;

public class TypeDeclarationScopeTests
{
    private static readonly IReadOnlyList<MetadataReference> DefaultReferences =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
    ];

    [Fact]
    public void GetCurrentTypeName_ReturnsEmptyWhenStackEmpty()
    {
        var scope = new TypeDeclarationScope();
        Assert.Equal(string.Empty, scope.GetCurrentTypeName());
    }

    [Fact]
    public void SanitizeTypeName_ReturnsTypeWhenNullOrWhitespace()
    {
        var scope = new TypeDeclarationScope();
        Assert.Equal("Type", scope.SanitizeTypeName(null));
        Assert.Equal("Type", scope.SanitizeTypeName("   "));
    }

    [Fact]
    public void SanitizeTypeName_ReplacesDotsWithUnderscore()
    {
        var scope = new TypeDeclarationScope();
        Assert.Equal("Foo_Bar", scope.SanitizeTypeName("Foo.Bar"));
    }

    [Fact]
    public void SanitizeTypeName_ReplacesSpacesAndTrimsUnderscores()
    {
        var scope = new TypeDeclarationScope();
        Assert.Equal("Foo_Bar", scope.SanitizeTypeName(" Foo Bar "));
    }

    [Fact]
    public void SanitizeTypeName_CollapsesConsecutiveUnderscores()
    {
        var scope = new TypeDeclarationScope();
        Assert.Equal("_Foo_Bar_Baz_", scope.SanitizeTypeName("__Foo__Bar__Baz__"));
    }

    [Fact]
    public void SanitizeTypeName_ReplacesHyphensWithUnderscore()
    {
        var scope = new TypeDeclarationScope();
        Assert.Equal("Foo_Bar", scope.SanitizeTypeName("Foo-Bar"));
    }

    [Fact]
    public void SanitizeTypeName_AllSeparatorsFallbackToType()
    {
        var scope = new TypeDeclarationScope();
        Assert.Equal("Type", scope.SanitizeTypeName("...---___   "));
    }

    [Fact]
    public void SanitizeTypeName_AllUnderscoresFallbackToType()
    {
        var scope = new TypeDeclarationScope();
        Assert.Equal("Type", scope.SanitizeTypeName("___"));
    }

    [Fact]
    public void SanitizeTypeName_PrefixesLeadingDigits()
    {
        var scope = new TypeDeclarationScope();
        Assert.Equal("_123Type", scope.SanitizeTypeName("123Type"));
    }

    [Fact]
    public void SanitizeTypeName_ReplacesPunctuationAndCollapses()
    {
        var scope = new TypeDeclarationScope();
        Assert.Equal("Foo_Bar_Baz", scope.SanitizeTypeName("Foo?Bar: Baz!!"));
    }

    [Fact]
    public void SanitizeTypeName_ReplacesSlashesAndColons()
    {
        var scope = new TypeDeclarationScope();
        Assert.Equal("Foo_Bar_Baz", scope.SanitizeTypeName("Foo/Bar: Baz"));
    }

    [Fact]
    public void SanitizeTypeName_PreservesLeadingUnderscore()
    {
        var scope = new TypeDeclarationScope();
        Assert.Equal("_LeadingName", scope.SanitizeTypeName("_LeadingName"));
    }

    [Fact]
    public void SanitizeTypeName_PreservesTrailingUnderscore()
    {
        var scope = new TypeDeclarationScope();
        Assert.Equal("Trailing_", scope.SanitizeTypeName("Trailing_"));
    }

    [Fact]
    public void SanitizeTypeName_LeavesValidIdentifierUntouched()
    {
        var scope = new TypeDeclarationScope();
        Assert.Equal("ValidName123", scope.SanitizeTypeName("ValidName123"));
    }

    [Fact]
    public void GetTypeName_UsesNestedTypeNames()
    {
        const string source = """
class Outer
{
    class Inner { }
}
""";

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = (CompilationUnitSyntax)tree.GetRoot();
        var compilation = CSharpCompilation.Create(
            "Sample",
            [tree],
            DefaultReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);

        var outerDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
        var innerDecl = outerDecl.DescendantNodes().OfType<ClassDeclarationSyntax>().First();

        var outerSymbol = (INamedTypeSymbol)model.GetDeclaredSymbol(outerDecl)!;
        var innerSymbol = (INamedTypeSymbol)model.GetDeclaredSymbol(innerDecl)!;

        var scope = new TypeDeclarationScope();
        using (scope.Push(outerSymbol))
        using (scope.Push(innerSymbol))
        {
            Assert.Equal("Outer_Inner", scope.GetCurrentTypeName());
            Assert.Equal("Outer", scope.GetTypeName(outerSymbol));
            Assert.Equal("Outer_Inner", scope.GetTypeName(innerSymbol));
        }
    }

    [Fact]
    public void GetTypeName_ReturnsEmptyForNullSymbol()
    {
        var scope = new TypeDeclarationScope();
        Assert.Equal(string.Empty, scope.GetTypeName(null!));
    }

    [Fact]
    public void GetCurrentTypeName_UpdatesAfterPop()
    {
        const string source = """
class Outer
{
    class Inner { }
}
""";

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = (CompilationUnitSyntax)tree.GetRoot();
        var compilation = CSharpCompilation.Create(
            "Sample",
            [tree],
            DefaultReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);

        var outerDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
        var innerDecl = outerDecl.DescendantNodes().OfType<ClassDeclarationSyntax>().First();

        var outerSymbol = (INamedTypeSymbol)model.GetDeclaredSymbol(outerDecl)!;
        var innerSymbol = (INamedTypeSymbol)model.GetDeclaredSymbol(innerDecl)!;

        var scope = new TypeDeclarationScope();
        using (scope.Push(outerSymbol))
        {
            var innerGuard = scope.Push(innerSymbol);
            Assert.Equal("Outer_Inner", scope.GetCurrentTypeName());

            innerGuard.Dispose();
            Assert.Equal("Outer", scope.GetCurrentTypeName());
        }
    }
}
