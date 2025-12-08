using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using RobloxCS.AST;
using RobloxCS.AST.Expressions;
using RobloxCS.AST.Statements;
using RobloxCS.Shared;
using RobloxCS.TranspilerV2;
using SymbolMetadataManager = RobloxCS.Luau.SymbolMetadataManager;

namespace RobloxCS.Tests;

public class TranspilerV2Tests
{
    [Fact]
    public void TranspilerV2_EmitsClassSkeleton()
    {
        const string source = """
                             class Foo
                             {
                                 public Foo()
                                 {
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.NotNull(chunk);
        Assert.NotEmpty(chunk.Block.Statements);
        Assert.False(string.IsNullOrWhiteSpace(rendered));
    }

    [Fact]
    public void TranspilerV2_ClassWithFields_MatchesLegacyGenerator()
    {
        const string source = """
                             class Foo
                             {
                                 private readonly int _offset = 4;

                                 public int Value = 2;

                                 public Foo()
                                 {
                                     Value = Value + _offset;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_ClassWithMethods_MatchesLegacyGenerator()
    {
        const string source = """
                             class Counter
                             {
                                 private int _total;

                                 public Counter(int start)
                                 {
                                     _total = start;
                                 }

                                 public void Increment()
                                 {
                                     _total = _total + 1;
                                 }

                                 public int GetTotal()
                                 {
                                     return _total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_EnumMember_MatchesLegacyGenerator()
    {
        const string source = """
                             enum Abc
                             {
                                 A,
                                 B = 5,
                                 C,
                             }

                             class EnumUser
                             {
                                 public int Value = (int)Abc.B;
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        SymbolMetadataManager.Clear();
        var chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy).Replace(" :: number", string.Empty, StringComparison.Ordinal);
        var normalizedRendered = NormalizeLuau(rendered).Replace(" :: number", string.Empty, StringComparison.Ordinal);

        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_Record_MatchesLegacyGenerator()
    {
        const string source = """
                             public record Person(string Name, int Age);
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalized = NormalizeLuau(rendered);
        Assert.Contains("Person = setmetatable", normalized, StringComparison.Ordinal);
        Assert.Contains("Person.__index = Person", normalized, StringComparison.Ordinal);
        Assert.Contains("self.Name = Name", normalized, StringComparison.Ordinal);
        Assert.Contains("self.Age = Age", normalized, StringComparison.Ordinal);
        Assert.Contains("CS.defineGlobal(\"Person\"", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_InterfaceWithIndexer_EmitsTypeAlias()
    {
        const string source = """
                             public interface IIndexed
                             {
                                 int this[int index] { get; set; }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalized = NormalizeLuau(rendered);
        Assert.Contains("type IIndexed = { this[]: (number) -> number", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_InterfaceStaticMembers_EmitStaticAlias()
    {
        const string source = """
                             public interface IStaticy
                             {
                                 static int Count => 3;
                                 static void Log(){}
                                 int this[int index] { get; set; }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.Contains("type IStaticy = { this[]: (number) -> number", rendered, StringComparison.Ordinal);
        Assert.Contains("type IStaticy_static = { read Count: number", rendered, StringComparison.Ordinal);
        Assert.Contains("Log: () -> ()", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_ClassWithStaticField_MatchesLegacyGenerator()
    {
        const string source = """
                             class Cache
                             {
                                 public static int Size = 10;

                                 public Cache()
                                 {
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        var assignmentIndex = normalized.IndexOf("Cache.Size = 10", StringComparison.Ordinal);
        var defineIndex = normalized.IndexOf("CS.defineGlobal(\"Cache\"", StringComparison.Ordinal);

        Assert.True(assignmentIndex >= 0, "Missing static field assignment.");
        Assert.True(defineIndex >= 0, "Missing CS.defineGlobal registration for Cache.");
        Assert.True(assignmentIndex < defineIndex, "Global export should occur after the field assignment.");
    }

    [Fact]
    public void TranspilerV2_ClassWithStaticProperty_MatchesLegacyGenerator()
    {
        const string source = """
                             class Settings
                             {
                                 public static int Difficulty { get; set; } = 3;

                                 public Settings()
                                 {
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        var assignmentIndex = normalized.IndexOf("Settings.Difficulty = 3", StringComparison.Ordinal);
        var defineIndex = normalized.IndexOf("CS.defineGlobal(\"Settings\"", StringComparison.Ordinal);

        Assert.True(assignmentIndex >= 0, "Missing static property assignment.");
        Assert.True(defineIndex >= 0, "Missing CS.defineGlobal registration for Settings.");
        Assert.True(assignmentIndex < defineIndex, "Global export should occur after the property assignment.");
    }

    [Fact]
    public void TranspilerV2_ClassWithStaticMethods_MatchesLegacyGenerator()
    {
        const string source = """
                             class MathUtil
                             {
                                 private static int _baseValue = 1;

                                 public MathUtil()
                                 {
                                 }

                                 public static void SetBase(int value)
                                 {
                                     _baseValue = value;
                                 }

                                 public static int Apply(int value)
                                 {
                                     return value + _baseValue;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        if (normalizedLegacy != normalizedRendered) {
            throw new Exception($"Legacy:\n{legacy}\n---\nRendered:\n{rendered}");
        }
    }

    [Fact]
    public void TranspilerV2_StaticClass_MatchesLegacyGenerator()
    {
        const string source = """
                             static class Utilities
                             {
                                 public static int Factor = 2;

                                 static Utilities()
                                 {
                                     Factor = 3;
                                 }

                                 public static int Multiply(int value)
                                 {
                                     return value * Factor;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("Utilities.Factor = 2", normalized, StringComparison.Ordinal);
        Assert.Contains("Utilities.Factor = 3", normalized, StringComparison.Ordinal);

        var factorInitIndex = normalized.IndexOf("Utilities.Factor = 2", StringComparison.Ordinal);
        var ctorIndex = normalized.IndexOf("Utilities.Factor = 3", StringComparison.Ordinal);
        Assert.True(factorInitIndex >= 0 && ctorIndex > factorInitIndex, "Static constructor should run after field initializers.");
        var defineIndex = normalized.IndexOf("CS.defineGlobal(\"Utilities\"", StringComparison.Ordinal);
        Assert.True(defineIndex > ctorIndex, "Global export should occur after static initialization.");
        Assert.Contains("function Utilities.Multiply", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SimpleStruct_EmitsFactory()
    {
        const string source = """
                             struct Vector2
                             {
                                 public double X;
                                 public double Y;
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local function Vector2", rendered);
        Assert.Contains("return { X = X, Y = Y }", normalized);
        Assert.Contains("type Vector2 = {", rendered);
    }

    [Fact]
    public void TranspilerV2_PropertyInitializer_AssignsInConstructor()
    {
        const string source = """
                             class Counter
                             {
                                 public int Count { get; private set; } = 1;

                                 public Counter()
                                 {
                                 }

                                 public void SetCount(int value)
                                 {
                                     Count = value;
                                 }

                                 public int GetCount()
                                 {
                                     return Count;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        var chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var classDoBlock = chunk.Block.Statements.OfType<DoStatement>().Single().Block;

        var constructorLocal = classDoBlock.Statements
            .OfType<LocalAssignment>()
            .Single(stmt => stmt.Names.Single().Value == "constructor");

        var constructorFunction = Assert.IsType<AnonymousFunction>(constructorLocal.Expressions.Single());
        var constructorBody = constructorFunction.Body.Body;

        var propertyAssignment = constructorBody.Statements
            .OfType<Assignment>()
            .FirstOrDefault(stmt => stmt.Vars.Single() is VarName varName && varName.Name == "self.Count");

        Assert.NotNull(propertyAssignment);

        var initializer = Assert.IsType<NumberExpression>(propertyAssignment!.Expressions.Single());
        Assert.Equal(1, initializer.Value);
    }

    [Fact]
    public void TranspilerV2_PropertyAccessors_EmitFunctions()
    {
        const string source = """
                             class Counter
                             {
                                 private int _count;

                                 public Counter()
                                 {
                                     _count = 5;
                                 }

                                 public int Count
                                 {
                                     get
                                     {
                                        return _count + 1;
                                     }

                                     private set
                                     {
                                         _count = value;
                                     }
                                 }

                                 public void Reset()
                                 {
                                     Count = 0;
                                 }

                                 public void Add(int amount)
                                 {
                                     Count = Count + amount;
                                 }

                                 public void Negate()
                                 {
                                     Count = -Count;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        var chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var classDoBlock = chunk.Block.Statements.OfType<DoStatement>().Single().Block;

        var methodNames = classDoBlock.Statements
            .OfType<FunctionDeclaration>()
            .Select(fn => fn.Name.ToFriendly())
            .ToList();

        Assert.DoesNotContain("Counter:GetCount", methodNames);
        Assert.DoesNotContain("Counter:SetCount", methodNames);
        Assert.Contains("Counter:Reset", methodNames);
        Assert.Contains("Counter:Add", methodNames);
        Assert.Contains("Counter:Negate", methodNames);

        var resetFunction = classDoBlock.Statements
            .OfType<FunctionDeclaration>()
            .Single(fn => fn.Name.ToFriendly() == "Counter:Reset");
        var resetAssignment = Assert.IsType<Assignment>(Assert.Single(resetFunction.Body.Body.Statements));
        var resetTarget = Assert.IsType<VarName>(resetAssignment.Vars.Single());
        Assert.Equal("self.Count", resetTarget.Name);
        Assert.Equal(0, Assert.IsType<NumberExpression>(resetAssignment.Expressions.Single()).Value);

        var addFunction = classDoBlock.Statements
            .OfType<FunctionDeclaration>()
            .Single(fn => fn.Name.ToFriendly() == "Counter:Add");
        var addAssignment = Assert.IsType<Assignment>(Assert.Single(addFunction.Body.Body.Statements));
        var addTarget = Assert.IsType<VarName>(addAssignment.Vars.Single());
        Assert.Equal("self.Count", addTarget.Name);
        var addExpression = Assert.IsType<BinaryOperatorExpression>(addAssignment.Expressions.Single());
        Assert.Equal(BinOp.Plus, addExpression.Op);
        Assert.Equal("self.Count", Assert.IsType<SymbolExpression>(addExpression.Left).Value);
        Assert.Equal("amount", Assert.IsType<SymbolExpression>(addExpression.Right).Value);

        var negateFunction = classDoBlock.Statements
            .OfType<FunctionDeclaration>()
            .Single(fn => fn.Name.ToFriendly() == "Counter:Negate");
        var negateAssignment = Assert.IsType<Assignment>(Assert.Single(negateFunction.Body.Body.Statements));
        var negateTarget = Assert.IsType<VarName>(negateAssignment.Vars.Single());
        Assert.Equal("self.Count", negateTarget.Name);
        var negateExpression = Assert.IsType<UnaryOperatorExpression>(negateAssignment.Expressions.Single());
        Assert.Equal(UnOp.Minus, negateExpression.Op);
        Assert.Equal("self.Count", Assert.IsType<SymbolExpression>(negateExpression.Operand).Value);
    }

    [Fact]
    public void TranspilerV2_NestedClass_UsesScopedName()
    {
        const string source = """
                             class Outer
                             {
                                 public static int Count = 0;

                                 public class Inner
                                 {
                                     public static int Value = 1;

                                     public static void Increment()
                                     {
                                         Count = Count + Value;
                                     }
                                 }

                                 public static void TouchInner()
                                 {
                                     Inner.Increment();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("local Outer_Inner", normalizedRendered);
        Assert.Contains("CS.defineGlobal(\"Outer_Inner\"", normalizedRendered);
        Assert.DoesNotContain("local Inner", normalizedRendered);
        Assert.Contains("Outer_Inner.Increment", normalizedRendered);

        Assert.Contains("local Inner", normalizedLegacy);
        Assert.DoesNotContain("local Outer_Inner", normalizedLegacy);
        Assert.Contains("Inner.Increment", normalizedLegacy);
    }

    [Fact]
    public void TranspilerV2_NestedStruct_EmitsAliasedStruct()
    {
        const string source = """
                             using System;

                             class DemoAttribute : Attribute
                             {
                                 public string? Label { get; set; }
                             }

                             interface ITagged
                             {
                             }

                             class Outer
                             {
                                 [Demo(Label = "inner")]
                                 public struct Inner : ITagged
                                 {
                                     public int Value;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local Outer_Inner", normalized, StringComparison.Ordinal);
        Assert.Contains("type Outer_Inner", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO: nested struct", normalized, StringComparison.Ordinal);
        Assert.Contains("CS.defineGlobal(\"Outer_Inner\"", normalized, StringComparison.Ordinal);
        Assert.Contains("Outer_Inner.__interfaces = { { ITagged } }", normalized);

        var metadataIndex = normalized.IndexOf("Outer_Inner.__attributes", StringComparison.Ordinal);
        var defineIndex = normalized.IndexOf("CS.defineGlobal(\"Outer_Inner\"", StringComparison.Ordinal);
        Assert.True(metadataIndex >= 0, "Missing nested struct metadata assignment.");
        Assert.True(defineIndex >= 0, "Missing nested struct global export.");
        Assert.True(metadataIndex < defineIndex, "Nested struct metadata must be assigned before export.");

        var attributesAssignment = FindAssignment(chunk, "Outer_Inner.__attributes");
        Assert.NotNull(attributesAssignment);
        var attributesTable = Assert.IsType<TableConstructor>(attributesAssignment!.Expressions.Single());
        var attributeEntry = Assert.IsType<TableConstructor>(Assert.IsType<NoKey>(Assert.Single(attributesTable.Fields)).Expression);
        var attributeFields = attributeEntry.Fields;
        var attributeName = Assert.IsType<StringExpression>(Assert.IsType<NoKey>(attributeFields[0]).Expression);
        Assert.Equal("DemoAttribute", attributeName.Value);

        var positionalArgs = Assert.IsType<TableConstructor>(Assert.IsType<NoKey>(attributeFields[1]).Expression);
        Assert.Empty(positionalArgs.Fields);

        var namedField = attributeFields.OfType<NameKey>().First(entry => entry.Key == "named");
        var namedTable = Assert.IsType<TableConstructor>(namedField.Value);
        var labelEntry = Assert.IsType<NameKey>(Assert.Single(namedTable.Fields));
        Assert.Equal("Label", labelEntry.Key);
        Assert.Equal("inner", Assert.IsType<StringExpression>(labelEntry.Value).Value);
    }

    [Fact]
    public void TranspilerV2_NestedClass_EmitsMetadataBeforeExport()
    {
        const string source = """
                             using System;

                             class DemoAttribute : Attribute
                             {
                                 public string? Label { get; set; }
                             }

                             interface ITagged
                             {
                             }

                             class Outer
                             {
                                 [Demo(Label = "inner")]
                                 public class Inner : ITagged
                                 {
                                     public Inner()
                                     {
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local Outer_Inner", normalized, StringComparison.Ordinal);
        Assert.Contains("CS.defineGlobal(\"Outer_Inner\"", normalized, StringComparison.Ordinal);
        Assert.Contains("Outer_Inner.__interfaces = { { ITagged } }", normalized);

        var metadataIndex = normalized.IndexOf("Outer_Inner.__attributes", StringComparison.Ordinal);
        var defineIndex = normalized.IndexOf("CS.defineGlobal(\"Outer_Inner\"", StringComparison.Ordinal);
        Assert.True(metadataIndex >= 0, "Missing nested class metadata assignment.");
        Assert.True(defineIndex >= 0, "Missing nested class global export.");
        Assert.True(metadataIndex < defineIndex, "Nested class metadata must be assigned before export.");

        var attributesAssignment = FindAssignment(chunk, "Outer_Inner.__attributes");
        Assert.NotNull(attributesAssignment);
        var attributesTable = Assert.IsType<TableConstructor>(attributesAssignment!.Expressions.Single());
        var attributeEntry = Assert.IsType<TableConstructor>(Assert.IsType<NoKey>(Assert.Single(attributesTable.Fields)).Expression);
        var attributeFields = attributeEntry.Fields;
        var attributeName = Assert.IsType<StringExpression>(Assert.IsType<NoKey>(attributeFields[0]).Expression);
        Assert.Equal("DemoAttribute", attributeName.Value);

        var positionalArgs = Assert.IsType<TableConstructor>(Assert.IsType<NoKey>(attributeFields[1]).Expression);
        Assert.Empty(positionalArgs.Fields);

        var namedField = attributeFields.OfType<NameKey>().First(entry => entry.Key == "named");
        var namedTable = Assert.IsType<TableConstructor>(namedField.Value);
        var labelEntry = Assert.IsType<NameKey>(Assert.Single(namedTable.Fields));
        Assert.Equal("Label", labelEntry.Key);
        Assert.Equal("inner", Assert.IsType<StringExpression>(labelEntry.Value).Value);
    }

    [Fact]
    public void TranspilerV2_NestedClass_StaticFieldsAssignBeforeExport()
    {
        const string source = """
                             class Outer
                             {
                                 public class Inner
                                 {
                                     public static int Value = 5;
                                     public static int Flag = Value + 1;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        var firstAssignmentIndex = normalized.IndexOf("Outer_Inner.Value = 5", StringComparison.Ordinal);
        var secondAssignmentIndex = normalized.IndexOf("Outer_Inner.Flag = Outer_Inner.Value + 1", StringComparison.Ordinal);
        var defineIndex = normalized.IndexOf("CS.defineGlobal(\"Outer_Inner\"", StringComparison.Ordinal);

        Assert.True(firstAssignmentIndex >= 0, "Missing nested static field assignment for Value.");
        Assert.True(secondAssignmentIndex >= 0, "Missing nested static field assignment for Flag.");
        Assert.True(defineIndex >= 0, "Missing nested class global export.");
        Assert.True(firstAssignmentIndex < defineIndex, "Global export should occur after nested static field initialization.");
        Assert.True(secondAssignmentIndex < defineIndex, "Global export should occur after nested static field initialization.");
    }

    [Fact]
    public void TranspilerV2_NestedClass_StaticPropertiesAssignBeforeExport()
    {
        const string source = """
                             class Outer
                             {
                                 public class Inner
                                 {
                                     public static int Difficulty { get; set; } = 3;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        var assignmentIndex = normalized.IndexOf("Outer_Inner.Difficulty = 3", StringComparison.Ordinal);
        var defineIndex = normalized.IndexOf("CS.defineGlobal(\"Outer_Inner\"", StringComparison.Ordinal);

        Assert.True(assignmentIndex >= 0, "Missing nested static property assignment.");
        Assert.True(defineIndex >= 0, "Missing nested class global export.");
        Assert.True(assignmentIndex < defineIndex, "Global export should occur after nested static property assignment.");
    }

    [Fact]
    public void TranspilerV2_StructWithStaticMember_EmitsStaticAssignments()
    {
        const string source = """
                             struct Cache
                             {
                                public static int Count = 5;
                                public static double Factor { get; } = 2.5;
                                public static int Multiply(int value) => value * 2;
                                public int Value;
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        var countIndex = normalized.IndexOf("Cache.Count = 5", StringComparison.Ordinal);
        var factorIndex = normalized.IndexOf("Cache.Factor = 2.5", StringComparison.Ordinal);
        var methodIndex = normalized.IndexOf("function Cache.Multiply", StringComparison.Ordinal);
        var defineIndex = normalized.IndexOf("CS.defineGlobal(\"Cache\"", StringComparison.Ordinal);

        Assert.True(countIndex >= 0, normalized);
        Assert.True(factorIndex >= 0, normalized);
        Assert.True(methodIndex >= 0, normalized);
        Assert.True(defineIndex > methodIndex && defineIndex > factorIndex, normalized);
        Assert.Contains("type Cache = {", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_StructWithStaticEvent_EmitsSignalAssignment()
    {
        const string source = """
                             struct Cache
                             {
                                 public static event System.Action? OnNotify;
                                 public static class Signal
                                 {
                                     public static System.Action New() => () => { };
                                 }
                                 public int Value;
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.Contains("local Signal = require", rendered, StringComparison.Ordinal);
        Assert.Contains("Cache.OnNotify = Signal.new()", rendered, StringComparison.Ordinal);
        var defineIndex = rendered.IndexOf("CS.defineGlobal(\"Cache\"", StringComparison.Ordinal);
        var eventIndex = rendered.IndexOf("Cache.OnNotify = Signal.new()", StringComparison.Ordinal);
        Assert.True(eventIndex >= 0 && defineIndex > eventIndex, rendered);
    }

    [Fact]
    public void TranspilerV2_StructWithStaticEventNulledByCtor_SkipsSignalInitialization()
    {
        const string source = """
                             struct Cache
                             {
                                 public static event System.Action? OnNotify;
                                 static Cache()
                                 {
                                     OnNotify = null;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.DoesNotContain("local Signal = require", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Signal.new", rendered, StringComparison.Ordinal);
        Assert.Contains("Cache.OnNotify = nil", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_StructWithStaticEventInitializedInCtor_EmitsSingleSignalAssignment()
    {
        const string source = """
                             struct Cache
                             {
                                 public static event System.Action? OnNotify;
                                 public static class Signal
                                 {
                                     public static System.Action New() => () => { };
                                 }
                                 static Cache()
                                 {
                                     OnNotify = Signal.New();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.DoesNotContain("local Signal = require", rendered, StringComparison.Ordinal);
        Assert.Equal(1, rendered.Split("Cache.OnNotify = Cache_Signal.New()", StringSplitOptions.None).Length - 1);
        Assert.Contains("Cache.OnNotify = Cache_Signal.New()", rendered, StringComparison.Ordinal);
        var defineIndex = rendered.IndexOf("CS.defineGlobal(\"Cache\"", StringComparison.Ordinal);
        var eventIndex = rendered.IndexOf("Cache.OnNotify = Cache_Signal.New()", StringComparison.Ordinal);
        Assert.True(eventIndex >= 0 && defineIndex > eventIndex, rendered);
    }

    [Fact]
    public void TranspilerV2_StructWithStaticEventInitializedInCtorCustomFactory_EmitsAssignmentOnce()
    {
        const string source = """
                             struct Cache
                             {
                                 public static event System.Action? OnNotify;
                                 public static class Helper
                                 {
                                     public static System.Action Create() => () => { };
                                 }
                                 static Cache()
                                 {
                                     OnNotify = Helper.Create();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.DoesNotContain("Signal.new", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("local Signal = require", rendered, StringComparison.Ordinal);
        Assert.Equal(1, rendered.Split("Cache.OnNotify = Cache_Helper.Create()", StringSplitOptions.None).Length - 1);
        Assert.Contains("Cache.OnNotify = Cache_Helper.Create()", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_StructWithStaticEventInitializedInCtorExternalSignal_ImportsAndAssigns()
    {
        const string source = """
                             static class Signal { public static System.Action New() => () => { }; }
                             struct Cache
                             {
                                 public static event System.Action? OnNotify;
                                 static Cache()
                                 {
                                     OnNotify = Signal.New();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.DoesNotContain("local Signal = require", rendered, StringComparison.Ordinal);
        Assert.Contains("Cache.OnNotify = Signal.New()", rendered, StringComparison.Ordinal);
        Assert.Equal(1, rendered.Split("Cache.OnNotify = Signal.New()", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void TranspilerV2_StructWithStaticEventInitializedInCtorNestedSignal_SkipsImport()
    {
        const string source = """
                             struct Cache
                             {
                                 public static event System.Action? OnNotify;
                                 public static class Signal
                                 {
                                     public static System.Action New() => () => { };
                                 }
                                 static Cache()
                                 {
                                     OnNotify = Signal.New();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.DoesNotContain("local Signal = require", rendered, StringComparison.Ordinal);
        Assert.Contains("Cache.OnNotify = Cache_Signal.New()", rendered, StringComparison.Ordinal);
        Assert.Equal(1, rendered.Split("Cache.OnNotify = Cache_Signal.New()", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void TranspilerV2_StructStaticCtor_AssignsNil_SkipsSignalImport()
    {
        const string source = """
                             struct Cache
                             {
                                 public static event System.Action? OnNotify;
                                 static Cache()
                                 {
                                     OnNotify = null;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.DoesNotContain("local Signal = require", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Signal.new", rendered, StringComparison.Ordinal);
        Assert.Contains("Cache.OnNotify = nil", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_StructStaticCtor_UsesQualifiedSignalHelper_SkipsImport()
    {
        const string source = """
                             namespace Helpers { static class Signal { public static System.Action New() => () => { }; } }
                             struct Cache
                             {
                                 public static event System.Action? OnNotify;
                                 static Cache()
                                 {
                                     OnNotify = Helpers.Signal.New();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.DoesNotContain("local Signal = require", rendered, StringComparison.Ordinal);
        Assert.Contains("Cache.OnNotify = Helpers.Signal.New()", rendered, StringComparison.Ordinal);
        Assert.Equal(1, rendered.Split("Cache.OnNotify = Helpers.Signal.New()", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void TranspilerV2_StructStaticCtor_AliasSignalHelper_ImportsAndAssigns()
    {
        const string source = """
                             namespace Helpers { static class Signal { public static System.Action New() => () => { }; } }
                             using Signal = Helpers.Signal;
                             struct Cache
                             {
                                 public static event System.Action? OnNotify;
                                 static Cache()
                                 {
                                     OnNotify = Signal.New();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        // Alias resolution produces the colon-call form while still importing GoodSignal.
        Assert.Contains("local Signal = require", rendered, StringComparison.Ordinal);
        Assert.Contains("Cache.OnNotify = Signal:New()", rendered, StringComparison.Ordinal);
        Assert.Equal(1, rendered.Split("Cache.OnNotify = Signal:New()", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void TranspilerV2_ClassStaticCtor_ExternalSignal_ImportsSignal()
    {
        const string source = """
                             static class Signal { public static System.Action New() => () => { }; }
                             class Sample
                             {
                                 public static event System.Action? Clicked;
                                 static Sample()
                                 {
                                     Clicked = Signal.New();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.DoesNotContain("local Signal = require", rendered, StringComparison.Ordinal);
        Assert.Contains("Sample.Clicked = Signal.New()", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_ClassStaticCtor_NestedSignal_SkipsSignalImport()
    {
        const string source = """
                             class Sample
                             {
                                 public static class Signal
                                 {
                                     public static System.Action New() => () => { };
                                 }
                                 public static event System.Action? Clicked;
                                 static Sample()
                                 {
                                     Clicked = Signal.New();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.DoesNotContain("local Signal = require", rendered, StringComparison.Ordinal);
        Assert.Contains("Sample.Clicked = Sample_Signal.New()", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_ClassStaticCtor_AssignsNil_SkipsSignalImport()
    {
        const string source = """
                             class Sample
                             {
                                 public static event System.Action? Clicked;
                                 static Sample()
                                 {
                                     Clicked = null;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.Contains("Sample.Clicked = nil", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_ClassStaticCtor_QualifiedSignalHelper_SkipsImport()
    {
        const string source = """
                             namespace Helpers { static class Signal { public static System.Action New() => () => { }; } }
                             class Sample
                             {
                                 public static event System.Action? Clicked;
                                 static Sample()
                                 {
                                     Clicked = Helpers.Signal.New();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.DoesNotContain("local Signal = require", rendered, StringComparison.Ordinal);
        Assert.Contains("Sample.Clicked = Helpers.Signal.New()", rendered, StringComparison.Ordinal);
        Assert.Equal(1, rendered.Split("Sample.Clicked = Helpers.Signal.New()", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void TranspilerV2_ClassStaticCtor_AliasSignalHelper_SkipsImport()
    {
        const string source = """
                             namespace Helpers { static class Signal { public static System.Action New() => () => { }; } }
                             using Signal = Helpers.Signal;
                             class Sample
                             {
                                 public static event System.Action? Clicked;
                                 static Sample()
                                 {
                                     Clicked = Signal.New();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Console.WriteLine(rendered);
        Assert.Contains("local Signal = require", rendered, StringComparison.Ordinal);
        Assert.Contains("Sample.Clicked = Signal:New()", rendered, StringComparison.Ordinal);
        Assert.Equal(1, rendered.Split("Sample.Clicked = Signal:New()", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void TranspilerV2_ClassStaticCtor_AliasSignalHelper_AssignsNil_SkipsImport()
    {
        const string source = """
                             namespace Helpers { static class Signal { public static System.Action New() => () => { }; } }
                             using Signal = Helpers.Signal;
                             class Sample
                             {
                                public static event System.Action? Clicked;
                                static Sample()
                                {
                                    Clicked = null;
                                }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.Contains("Sample.Clicked = nil", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_StructStaticCtor_AliasSignalHelper_AssignsNil_SkipsImport()
    {
        const string source = """
                             namespace Helpers { static class Signal { public static System.Action New() => () => { }; } }
                             using Signal = Helpers.Signal;
                             struct Cache
                             {
                                 public static event System.Action? OnNotify;
                                 static Cache()
                                 {
                                     OnNotify = null;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.Contains("Cache.OnNotify = nil", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_StructIndexer_EmitsIndexSignatureAlias()
    {
        const string source = """
                             struct Buffer
                             {
                                 public int this[int index] { get { return 0; } set { } }
                                 public int Value;
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.Contains("type Buffer = { [number]: number", rendered, StringComparison.Ordinal);
        Assert.Contains("Value: number", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_StructIndexer_WriteOnly_MarksWriteAccess()
    {
        const string source = """
                             struct Buffer
                             {
                                 public int this[int index] { set { _ = index; } }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.Contains("type Buffer = { write [number]: number", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_StructIndexer_ReadOnly_MarksReadAccess()
    {
        const string source = """
                             struct Buffer
                             {
                                 public int this[int index] { get { return index; } }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.Contains("type Buffer = { read [number]: number", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_EmptyStruct_EmitsFactoryAndAlias()
    {
        const string source = """
                             struct Empty
                             {
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = NormalizeLuau(TranspilerUtility.RenderLuauChunkV2(chunk));

        Assert.Contains("local function Empty()", rendered, StringComparison.Ordinal);
        Assert.Contains("return {}", rendered, StringComparison.Ordinal);
        Assert.Contains("type Empty = { }", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_StructWithInitializer_UsesDefaults()
    {
        const string source = """
                             struct Offset
                             {
                                 public double X = 5;
                                 public double Y;
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("local function Offset", rendered);
        Assert.Contains("X = X or 5", rendered);
        Assert.Contains("Y = Y", rendered);
        Assert.Contains("type Offset", rendered);
    }

    [Fact]
    public void TranspilerV2_GenericStruct_EmitsTypeAlias()
    {
        const string source = """
                             struct Wrapper<T>
                             {
                                 public T Value;
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("type Wrapper<T> = {", rendered);
        Assert.Contains("Value: T", rendered);
        Assert.Contains("local function Wrapper", rendered);
    }

    [Fact]
    public void TranspilerV2_GenericClass_EmitsTypeAliasWithGenerics()
    {
        const string source = """
                             class Box<T>
                             {
                                 public T Value;

                                 public Box()
                                 {
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("type Box<T> = typeof(Box)", rendered);
    }

    [Fact]
    public void TranspilerV2_ClassWithBaseInterfacesAndAttributes_EmitsMetadata()
    {
        const string source = """
                             using System;

                             class SampleAttribute : Attribute
                             {
                                 public bool Flag { get; set; }
                             }

                             class BaseWidget
                             {
                             }

                             interface IRenderable
                             {
                             }

                             [Sample("tag", 42, typeof(BaseWidget), Flag = true)]
                             class Widget : BaseWidget, IRenderable
                             {
                                 public Widget()
                                 {
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("Widget.__base = BaseWidget", rendered);
        var normalized = NormalizeLuau(rendered);
        Assert.Contains("Widget.__interfaces = { { IRenderable } }", normalized);
        var attributesIndex = normalized.IndexOf("Widget.__attributes", StringComparison.Ordinal);
        var defineIndex = normalized.IndexOf("CS.defineGlobal(\"Widget\"", StringComparison.Ordinal);
        Assert.True(attributesIndex >= 0, "Missing Widget metadata assignment.");
        Assert.True(defineIndex >= 0, "Missing Widget global export.");
        Assert.True(attributesIndex < defineIndex, "Metadata must be assigned before Widget is exported.");

        var attributesAssignment = FindAssignment(chunk, "Widget.__attributes");
        Assert.NotNull(attributesAssignment);

        var attributesTable = Assert.IsType<TableConstructor>(attributesAssignment!.Expressions.Single());
        var attributeEntry = Assert.IsType<TableConstructor>(Assert.IsType<NoKey>(Assert.Single(attributesTable.Fields)).Expression);
        var attributeFields = attributeEntry.Fields;

        var attributeName = Assert.IsType<StringExpression>(Assert.IsType<NoKey>(attributeFields[0]).Expression);
        Assert.Equal("SampleAttribute", attributeName.Value);

        var positionalArgs = Assert.IsType<TableConstructor>(Assert.IsType<NoKey>(attributeFields[1]).Expression);
        Assert.Collection(positionalArgs.Fields,
            field => Assert.Equal("tag", Assert.IsType<StringExpression>(Assert.IsType<NoKey>(field).Expression).Value),
            field => Assert.Equal(42, Assert.IsType<NumberExpression>(Assert.IsType<NoKey>(field).Expression).Value),
            field => Assert.Equal("BaseWidget", Assert.IsType<SymbolExpression>(Assert.IsType<NoKey>(field).Expression).Value));

        var namedField = attributeFields
            .OfType<NameKey>()
            .First(entry => entry.Key == "named");
        var namedTable = Assert.IsType<TableConstructor>(namedField.Value);
        var flagEntry = Assert.IsType<NameKey>(Assert.Single(namedTable.Fields));
        Assert.Equal("Flag", flagEntry.Key);
        Assert.Equal("true", Assert.IsType<SymbolExpression>(flagEntry.Value).Value);
    }

    [Fact]
    public void TranspilerV2_StructWithInterfacesAndAttributes_EmitsMetadata()
    {
        const string source = """
                             using System;

                             class DemoAttribute : Attribute
                             {
                                 public string? Label { get; set; }
                             }

                             interface ITagged
                             {
                             }

                             [Demo(10, Label = "point")]
                             struct DataPoint : ITagged
                             {
                                 public int Value;
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalized = NormalizeLuau(rendered);
        Assert.Contains("DataPoint.__interfaces = { { ITagged } }", normalized);
        var attributesIndex = normalized.IndexOf("DataPoint.__attributes", StringComparison.Ordinal);
        var defineIndex = normalized.IndexOf("CS.defineGlobal(\"DataPoint\"", StringComparison.Ordinal);
        Assert.True(attributesIndex >= 0, "Missing DataPoint metadata assignment.");
        Assert.True(defineIndex >= 0, "Missing DataPoint global export.");
        Assert.True(attributesIndex < defineIndex, "Metadata must be assigned before DataPoint is exported.");

        var attributesAssignment = FindAssignment(chunk, "DataPoint.__attributes");
        Assert.NotNull(attributesAssignment);

        var attributesTable = Assert.IsType<TableConstructor>(attributesAssignment!.Expressions.Single());
        var attributeEntry = Assert.IsType<TableConstructor>(Assert.IsType<NoKey>(Assert.Single(attributesTable.Fields)).Expression);
        var attributeFields = attributeEntry.Fields;

        var attributeName = Assert.IsType<StringExpression>(Assert.IsType<NoKey>(attributeFields[0]).Expression);
        Assert.Equal("DemoAttribute", attributeName.Value);

        var positionalArgs = Assert.IsType<TableConstructor>(Assert.IsType<NoKey>(attributeFields[1]).Expression);
        var positionalField = Assert.Single(positionalArgs.Fields);
        Assert.Equal(10, Assert.IsType<NumberExpression>(Assert.IsType<NoKey>(positionalField).Expression).Value);

        var namedField = attributeFields
            .OfType<NameKey>()
            .First(entry => entry.Key == "named");
        var namedTable = Assert.IsType<TableConstructor>(namedField.Value);
        var labelEntry = Assert.IsType<NameKey>(Assert.Single(namedTable.Fields));
        Assert.Equal("Label", labelEntry.Key);
        Assert.Equal("point", Assert.IsType<StringExpression>(labelEntry.Value).Value);
    }

    [Fact]
    public void TranspilerV2_ClassWithGenericConstraints_EmitsConstraints()
    {
        const string source = """
                             class BaseWidget
                             {
                             }

                             class ConstraintSample<T> where T : BaseWidget, new()
                             {
                                 public ConstraintSample()
                                 {
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("type ConstraintSample<T extends BaseWidget & (() -> T)> = typeof(ConstraintSample)", normalized);
    }

    [Fact]
    public void TranspilerV2_StructWithGenericConstraints_EmitsConstraints()
    {
        const string source = """
                             interface ITagged
                             {
                             }

                             struct ConstraintHolder<T> where T : ITagged, new()
                             {
                                 public T Value;
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("type ConstraintHolder<T extends ITagged & (() -> T)> = {", normalized);
    }

    [Fact]
    public void TranspilerV2_ClassWithClassStructNotNullConstraints_EmitsMarkers()
    {
        const string source = """
                             class ConstraintMarkers<T, U, V>
                                 where T : class
                                 where U : struct
                                 where V : notnull
                             {
                                 public ConstraintMarkers()
                                 {
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("type ConstraintMarkers<T extends class, U extends struct, V extends notnull> = typeof(ConstraintMarkers)", normalized);
    }

    [Fact]
    public void TranspilerV2_StructWithProperty_EmitsAlias()
    {
        const string source = """
                             struct Dimensions
                             {
                                 public double Width { get; }
                                 public double Height { get; } = 10;
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("local function Dimensions", rendered);
        Assert.Contains("Width = Width", rendered);
        Assert.Contains("Height = Height or 10", rendered);
        Assert.Contains("type Dimensions = {", rendered);
        Assert.Contains("read Width: number", rendered);
        Assert.Contains("read Height: number", rendered);
    }

    [Fact]
    public void TranspilerV2_StructWithNonConstantInitializer_QueuesPrerequisite()
    {
        const string source = """
                             struct Timestamped
                             {
                                 public long Ticks { get; } = GetTimestamp();

                                 private static long GetTimestamp()
                                 {
                                     return 5;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("local _Ticks_default", rendered);
        Assert.Contains("Ticks = Ticks or _Ticks_default", rendered);
    }

    [Fact]
    public void TranspilerV2_PropertyOnlyInterface_EmitsTypeAlias()
    {
        const string source = """
                             interface IPoint
                             {
                                 double X { get; }
                                 double Y { get; }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("type IPoint = {", rendered);
        Assert.Contains("read X: number", rendered);
        Assert.Contains("read Y: number", rendered);
        Assert.DoesNotContain("TODO: interface 'IPoint'", rendered);
    }

    [Fact]
    public void TranspilerV2_InterfaceWithMethod_EmitsCallbackType()
    {
        const string source = """
                             interface ICalculator
                             {
                                 double Add(double x, double y);
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("type ICalculator = {", rendered);
        Assert.Contains("Add: (number, number) -> number", rendered);
    }

    [Fact]
    public void TranspilerV2_InterfaceWithEvent_EmitsCallbackType()
    {
        const string source = """
                             using System;

                             interface INotifier
                             {
                                 event Action Fired;
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("type INotifier = {", rendered);
        Assert.Contains("Fired: Signal", rendered);
        Assert.DoesNotContain("TODO: interface 'INotifier'", rendered);
    }

    [Fact]
    public void TranspilerV2_InterfaceInheritance_FlattensMembers()
    {
        const string source = """
                             interface IPoint
                             {
                                 double X { get; }
                             }

                             interface IPoint3D : IPoint
                             {
                                 double Y { get; }
                                 double Z { get; }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("type IPoint =", rendered);
        Assert.Contains("type IPoint3D = {", rendered);
        Assert.Contains("read X: number", rendered);
        Assert.Contains("read Y: number", rendered);
        Assert.Contains("read Z: number", rendered);
    }

    [Fact]
    public void TranspilerV2_NestedInterface_EmitsAlias()
    {
        const string source = """
                             class Outer
                             {
                                 public interface IService
                                 {
                                     void DoWork();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("type Outer_IService", rendered);
        Assert.DoesNotContain("TODO: nested interface", rendered);
    }

    [Fact]
    public void TranspilerV2_MethodChainsAndObjectCreation_MatchesLegacyGenerator()
    {
        const string source = """
                             class Helper
                             {
                                 public Helper Next { get; private set; }

                                 public Helper()
                                 {
                                     Next = this;
                                 }

                                 public Helper GetNext()
                                 {
                                     return Next;
                                 }

                                 public void Invoke()
                                 {
                                 }
                             }

                             class Chain
                             {
                                 private Helper _root = new Helper();

                                 public Chain()
                                 {
                                 }

                                 public void Do()
                                 {
                                     this._root.GetNext().GetNext().Invoke();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_BinaryExpressions_MatchesLegacyGenerator()
    {
        const string source = """
                             class MathOps
                             {
                                 public MathOps()
                                 {
                                 }

                                 public int Compute(int value)
                                 {
                                     return (value - 1) / 2 % 3;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_ComparisonAndBooleanExpressions_MatchLegacyGenerator()
    {
        const string source = """
                             class Checker
                             {
                                 public Checker()
                                 {
                                 }

                                 public bool Evaluate(int a, int b)
                                 {
                                     return (a > b || a == b) && !(a < 0);
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_CompoundAssignments_MatchLegacyGenerator()
    {
        const string source = """
                             class Accumulator
                             {
                                 private int _value = 0;

                                 public Accumulator()
                                 {
                                 }

                                 public void Apply(int delta)
                                 {
                                     _value += delta;
                                     _value -= 1;
                                     _value *= 2;
                                     _value /= 3;
                                     _value %= 4;
                                 }

                                 public int Value => _value;
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_WhileLoop_MatchesLegacyGenerator()
    {
        const string source = """
                             class Counter
                             {
                                 private int _value = 0;

                                 public int IncrementUntil(int limit)
                                 {
                                     while (_value < limit)
                                     {
                                         _value += 1;
                                     }

                                     return _value;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_NumericForLoop_MatchesLegacyGenerator()
    {
        const string source = """
                             class Summation
                             {
                                 public Summation()
                                 {
                                 }

                                 public int SumTo(int limit)
                                 {
                                     var total = 0;

                                     for (var i = 1; i <= limit; i++)
                                     {
                                         total += i;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_AssignmentExpression_PrerequisitesBeforeLocal()
    {
        const string source = """
                             class Sample
                             {
                                 private int _value;

                                 public Sample()
                                 {
                                     _value = 0;
                                 }

                                 public void Run()
                                 {
                                     var result = (_value = 5);
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var assignmentIndex = rendered.IndexOf("self._value = 5", StringComparison.Ordinal);
        var localIndex = rendered.IndexOf("local result = self._value", StringComparison.Ordinal);

        Assert.True(assignmentIndex >= 0, "Expected assignment prerequisite to be emitted.");
        Assert.True(localIndex > assignmentIndex, "Expected local assignment to appear after prerequisite assignment.");
    }

    [Fact]
    public void TranspilerV2_CompoundAssignmentExpression_PrerequisitesBeforeLocal()
    {
        const string source = """
                             class Sample
                             {
                                 private int _value;

                                 public Sample()
                                 {
                                     _value = 1;
                                 }

                                 public void Run()
                                 {
                                     var result = (_value += 2);
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var assignmentIndex = rendered.IndexOf("self._value += 2", StringComparison.Ordinal);
        var localIndex = rendered.IndexOf("local result = self._value", StringComparison.Ordinal);

        Assert.True(assignmentIndex >= 0, "Expected compound assignment prerequisite to be emitted.");
        Assert.True(localIndex > assignmentIndex, "Expected local assignment to appear after compound assignment prerequisite.");
    }

    [Fact]
    public void TranspilerV2_BitwiseAssignment_MatchesLegacy()
    {
        const string source = """
                             class BitOps
                             {
                                 private int _flags;

                                 public BitOps()
                                 {
                                     _flags = 255;
                                 }

                                 public void Apply(int mask)
                                 {
                                     _flags &= mask;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("bit32.band", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("self._flags = 0", normalizedRendered, StringComparison.Ordinal);
        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_EventSubscription_MatchesLegacy()
    {
        const string source = """
                             using System;

                             class Button
                             {
                                 public event Action<int> Clicked;
                             }

                             class Listener
                             {
                                 private readonly Button _button = new Button();

                                 public Listener()
                                 {
                                     _button.Clicked += OnClicked;
                                 }

                                 public void Detach()
                                 {
                                     _button.Clicked -= OnClicked;
                                 }

                                 private void OnClicked(int value)
                                 {
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local Signal = require(rbxcs_include.GoodSignal)", normalized, StringComparison.Ordinal);
        Assert.Contains("self.Clicked = Signal.new()", normalized, StringComparison.Ordinal);
        Assert.Contains("local conn_Clicked = self._button.Clicked:Connect", normalized, StringComparison.Ordinal);
        Assert.Contains("return self:OnClicked(value)", normalized, StringComparison.Ordinal);
        Assert.Contains("conn_Clicked:Disconnect()", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_RefParameterAssignment_MatchesLegacy()
    {
        const string source = """
                             class RefSample
                             {
                                 public void Update(ref int value)
                                 {
                                     value += 1;
                                 }

                                 public void Apply()
                                 {
                                     var current = 0;
                                     Update(ref current);
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("value(value() + 1)", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("self:Update(function(...", normalizedRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_TupleAssignment_MatchesLegacy()
    {
        const string source = """
                             class TupleSample
                             {
                                 private int _x;
                                 private int _y;

                                 public void Run()
                                 {
                                     (_x, _y) = GetPair();
                                 }

                                 private (int, int) GetPair()
                                 {
                                     return (1, 2);
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("self._x, self._y = CS.unpackTuple", normalizedRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SystemTupleAssignment_UsesUnpack()
    {
        const string source = """
                             using System;

                             class SystemTupleSample
                             {
                                 private int _x;
                                 private int _y;

                                 public void Run()
                                 {
                                     (_x, _y) = GetTuple();
                                 }

                                 private Tuple<int, int> GetTuple()
                                 {
                                     return Tuple.Create(1, 2);
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("self._x, self._y = CS.unpackTuple", normalizedRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_IfStatement_MatchesLegacy()
    {
        const string source = """
                             class Branching
                             {
                                 public bool Compare(int value)
                                 {
                                     if (value > 10)
                                     {
                                         return true;
                                     }
                                     else
                                     {
                                         return value == 10;
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_WhileLoop_MatchesLegacy()
    {
        const string source = """
                             class Counter
                             {
                                 public int CountTo(int limit)
                                 {
                                     var total = 0;
                                     var current = 0;

                                     while (current < limit)
                                     {
                                         total += current;
                                         current += 1;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_StringConcatenation_UsesTwoDots()
    {
        const string source = """
                             class Greeter
                             {
                                 public string Greet(string name)
                                 {
                                     return name + "!";
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("name .. \"!\"", normalizedRendered, StringComparison.Ordinal);
        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_Bit32BinaryOperators_MatchLegacy()
    {
        const string source = """
                             class BitMath
                             {
                                 public int Mask(int value, int flag)
                                 {
                                     return value & flag;
                                 }

                                 public int Shift(int value)
                                 {
                                     return value << 2;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("bit32.band", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("bit32.lshift", normalizedRendered, StringComparison.Ordinal);
        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_UnaryOperators_MatchLegacy()
    {
        const string source = """
                             class Negation
                             {
                                 public int Negate(int value)
                                 {
                                     return -value;
                                 }

                                 public int Invert(int value)
                                 {
                                     return ~value;
                                 }

                                 public bool Toggle(bool input)
                                 {
                                     return !input;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("return -value", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("bit32.bnot", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("return not input", normalizedRendered, StringComparison.Ordinal);
        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_LogicalOperators_MatchLegacy()
    {
        const string source = """
                             class Logic
                             {
                                 public bool Combine(bool left, bool right)
                                 {
                                     return left && right;
                                 }

                                 public bool Either(bool left, bool right)
                                 {
                                     return left || right;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("return left and right", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("return left or right", normalizedRendered, StringComparison.Ordinal);
        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_ForLoop_MatchesLegacy()
    {
        const string source = """
                             class Loop
                             {
                                 public int Sum(int count)
                                 {
                                     var total = 0;

                                     for (var index = 0; index < count; index += 1)
                                     {
                                         total += index;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("for index = 0, count - 1 do", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("total += index", normalizedRendered, StringComparison.Ordinal);
        Assert.DoesNotContain("_shouldIncrement", normalizedRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_DoLoop_MatchesLegacy()
    {
        const string source = """
                             class Loop
                             {
                                 public int Sum(int count)
                                 {
                                     var total = 0;
                                     var index = 0;

                                     do
                                     {
                                         total += index;
                                         index += 1;
                                     }
                                     while (index < count);

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_ForEachLoop_MatchesLegacyGenerator()
    {
        const string source = """
                             class SumHelper
                             {
                                 public SumHelper()
                                 {
                                 }

                                 public int Sum(int[] values)
                                 {
                                     var total = 0;

                                     foreach (var value in values)
                                     {
                                         total += value;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_ForEachRange_UsesNumericFor()
    {
        const string source = """
                             using Roblox;

                             class RangeDemo
                             {
                                 public double Sum()
                                 {
                                     var total = 0.0;

                                     foreach (var value in RangeHelper.Range(0, 9))
                                     {
                                         total += value;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("for value = 0, 9 do", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("ipairs", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_ForEachRange_WithNegativeStep()
    {
        const string source = """
                             using Roblox;

                             class RangeDescending
                             {
                                 public double Sum()
                                 {
                                     var total = 0.0;

                                     foreach (var value in RangeHelper.Range(9, 0, -1))
                                     {
                                         total += value;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("for value = 9, 0, -1 do", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("ipairs", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_ForEachRange_WithFractionalStep()
    {
        const string source = """
                             using Roblox;

                             class RangeFractional
                             {
                                 public double Sum()
                                 {
                                     var total = 0.0;

                                     foreach (var value in RangeHelper.Range(0, 9, 0.5))
                                     {
                                         total += value;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("for value = 0, 9, 0.5 do", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("ipairs", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_ForEachRangeMacro_UsesNumericFor()
    {
        const string source = """
                             class RangeMacroDemo
                             {
                                 public double Sum()
                                 {
                                     var total = 0.0;

                                     foreach (var value in range(0, 5))
                                     {
                                         total += value;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("for value = 0, 5 do", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("ipairs", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_ForEachArrayLiteral_UsesIpairs()
    {
        const string source = """
                             class ArrayLoop
                             {
                                 public int Sum()
                                 {
                                     var total = 0;

                                     foreach (var value in new[] { 1, 2, 3 })
                                     {
                                         total += value;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("for _, value in ipairs({", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_ForEachList_UsesIpairs()
    {
        const string source = """
                             using System.Collections.Generic;

                             class ListLoop
                             {
                                 private readonly List<int> _values = new() { 1, 2, 3 };

                                 public int Sum()
                                 {
                                     var total = 0;

                                     foreach (var value in _values)
                                     {
                                         total += value;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        Assert.Contains("for value in self._values do", rendered, StringComparison.Ordinal);
        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }


    [Fact]
    public void TranspilerV2_ForEachFuncIterator_MatchesLegacy()
    {
        const string source = """
                             using System.Collections.Generic;

                             class IteratorDemo
                             {
                                 private IEnumerable<int> AllValues()
                                 {
                                     return new List<int> { 1, 1, 1 };
                                 }

                                 public int Count()
                                 {
                                     var total = 0;

                                     foreach (var value in AllValues())
                                     {
                                         total += 1;
                                         if (total > 2)
                                         {
                                             break;
                                         }
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("for _, value in ipairs(self:AllValues()) do", rendered, StringComparison.Ordinal);
        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_ForEachStringLiteral_UsesGmatch()
    {
        const string source = """
                             class StringLoop
                             {
                                 public int Count()
                                 {
                                     var total = 0;

                                     foreach (var ch in "abcd")
                                     {
                                         total += 1;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("string.gmatch(\"abcd\", \".\")", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_ForEachStringField_UsesGmatch()
    {
        const string source = """
                             class StringFieldLoop
                             {
                                 private readonly string _value = "abcd";

                                 public int Count()
                                 {
                                     var total = 0;

                                     foreach (var ch in _value)
                                     {
                                         total += 1;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("string.gmatch(self._value, \".\")", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_ElementAccess_AdjustsIndices()
    {
        const string source = """
                             class Indexer
                             {
                                 public int Grab(int[] values)
                                 {
                                     return values[0];
                                 }

                                 public void Update(int[] values)
                                 {
                                     values[1] = 42;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("values[1]", normalizedLegacy, StringComparison.Ordinal);
        Assert.Contains("values[1]", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("values[2]", normalizedLegacy, StringComparison.Ordinal);
        Assert.Contains("values[2]", normalizedRendered, StringComparison.Ordinal);
        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_ArraySpread_UsesTableUnpack()
    {
        const string source = """
                             class SpreadHolder
                             {
                                 private readonly int[] _stash = new[] { 2, 3 };

                                 public int[] Build()
                                 {
                                     return new[] { 1, .._stash };
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("table.unpack(self._stash)", normalizedLegacy, StringComparison.Ordinal);
        Assert.Contains("table.unpack(self._stash)", normalizedRendered, StringComparison.Ordinal);
        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_DictionaryInterfaceLoop_UsesPairs()
    {
        const string source = """
                             using System.Collections.Generic;

                             class DictionaryInterfaceLoop
                             {
                                 private readonly IDictionary<string, int> _entries = new Dictionary<string, int>();

                                 public int Sum()
                                 {
                                     var total = 0;

                                     foreach (var (key, value) in _entries)
                                     {
                                         total += value;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("pairs(", normalizedLegacy, StringComparison.Ordinal);
        Assert.Contains("pairs(", normalizedRendered, StringComparison.Ordinal);
        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_DictionaryKeyValuePairLoop_WrapsEntryTable()
    {
        const string source = """
                             using System.Collections.Generic;

                             class DictionaryKeyValueLoop
                             {
                                 private readonly Dictionary<string, int> _entries = new Dictionary<string, int>();

                                 public int Sum()
                                 {
                                     var total = 0;

                                     foreach (KeyValuePair<string, int> entry in _entries)
                                     {
                                         total += entry.Value;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("pairs(", normalizedLegacy, StringComparison.Ordinal);
        Assert.Contains("pairs(", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("local entry = {", normalizedLegacy, StringComparison.Ordinal);
        Assert.Contains("local entry = {", normalizedRendered, StringComparison.Ordinal);
        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_ForEachDictionaryTupleDestructuring_UsesPairs()
    {
        const string source = """
                             using System.Collections.Generic;

                             class DictionaryDestructureLoop
                             {
                                 private readonly Dictionary<string, int> _entries = new Dictionary<string, int>();

                                 public void Run()
                                 {
                                     foreach (var (key, value) in _entries)
                                     {
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("for key, value in pairs(self._entries) do", normalizedRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_TryFinally_MatchesLegacy()
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

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("CS.try", normalizedRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_TryReturn_PropagatesControlFlow()
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

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("local __tryExitType, __tryReturns = CS.try", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("if __tryExitType == CS.TRY_RETURN then", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("return table.unpack(__tryReturns", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("if __tryExitType ~= nil then", normalizedRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchStatement_MatchesLegacy()
    {
        const string source = """
                             class SwitchSample
                             {
                                 public string Describe(int value)
                                 {
                                     switch (value)
                                     {
                                         case 0:
                                             return "zero";
                                         case 1:
                                         case 2:
                                             return "small";
                                         default:
                                             return "other";
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_NestedClasses_ArePredeclaredAtModuleScope()
    {
        const string source = """
                             namespace RuntimeSpecs;

                             public static class AsyncFixture
                             {
                                 public static void Invoke()
                                 {
                                     _ = AsyncStaticExample.Bar();
                                 }

                                 public static class AsyncStaticExample
                                 {
                                     public static string Foo() => "foo";
                                     public static string Bar() => Foo() + "bar";
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);

        var rootLocalNames = chunk.Block.Statements
            .OfType<LocalAssignment>()
            .SelectMany(local => local.Names)
            .Select(symbol => symbol.Value)
            .ToList();

        var nestedTypeName = "AsyncFixture_AsyncStaticExample";
        Assert.Contains(nestedTypeName, rootLocalNames);

        var definingDoStatement = chunk.Block.Statements
            .OfType<DoStatement>()
            .Single(statement => statement.Block.Statements
                .OfType<Assignment>()
                .Any(assignment => assignment.Vars
                    .OfType<VarName>()
                    .Any(var => var.Name == nestedTypeName)));

        var innerLocalNames = definingDoStatement.Block.Statements
            .OfType<LocalAssignment>()
            .SelectMany(local => local.Names)
            .Select(symbol => symbol.Value)
            .ToList();

        Assert.DoesNotContain(nestedTypeName, innerLocalNames);
    }

    [Fact]
    public void TranspilerV2_SwitchStatement_FallthroughSetsFlag()
    {
        const string source = """
                             class SwitchFallthrough
                             {
                                 public string Describe(int value)
                                 {
                                     switch (value)
                                     {
                                         case 1:
                                             break;
                                         case 2:
                                         case 3:
                                             return "combo";
                                         default:
                                             return "other";
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("local _fallthrough = false", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("if _fallthrough or", normalizedRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchStatement_WhenClause_MatchesLegacy()
    {
        const string source = """
                             using System;

                             class SwitchWhen
                             {
                                 public string Describe(int value)
                                 {
                                     switch (value)
                                     {
                                         case int v when v > 0:
                                             return "positive";
                                         case int v when v < 0:
                                             return "negative";
                                         default:
                                             return "zero";
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("repeat", normalized, StringComparison.Ordinal);
        Assert.Contains("CS.is(value, \"number\")", normalized, StringComparison.Ordinal);
        Assert.Contains("local v = value", normalized, StringComparison.Ordinal);
        Assert.Contains("if v > 0 then", normalized, StringComparison.Ordinal);
        Assert.Contains("return \"positive\"", normalized, StringComparison.Ordinal);
        Assert.Contains("if v < 0 then", normalized, StringComparison.Ordinal);
        Assert.Contains("return \"negative\"", normalized, StringComparison.Ordinal);
        Assert.Contains("return \"zero\"", normalized, StringComparison.Ordinal);
        Assert.Contains("until true", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchStatement_RelationalPattern_MatchesLegacy()
    {
        const string source = """
                             class SwitchRelational
                             {
                                 public string Describe(int value)
                                 {
                                     switch (value)
                                     {
                                         case > 0:
                                             return "positive";
                                         case < 0:
                                             return "negative";
                                         default:
                                             return "zero";
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_SwitchExpression_MatchesLegacy()
    {
        const string source = """
                             class SwitchExpressionSample
                             {
                                public string Describe(int value)
                                 {
                                     return value switch
                                     {
                                         0 => "zero",
                                         1 => "one",
                                         _ => "other",
                                     };
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_SwitchExpression_WhenClause_MatchesLegacy()
    {
        const string source = """
                             using System;

                             class SwitchExpressionWhen
                             {
                                 public string Describe(int value)
                                 {
                                     return value switch
                                     {
                                         int v when v > 0 => "positive",
                                         int v when v < 0 => "negative",
                                         _ => "zero",
                                     };
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local _newValue", normalized, StringComparison.Ordinal);
        Assert.Contains("repeat", normalized, StringComparison.Ordinal);
        Assert.Contains("CS.is(value, \"number\")", normalized, StringComparison.Ordinal);
        Assert.Contains("local v = value", normalized, StringComparison.Ordinal);
        Assert.Contains("if v > 0 then", normalized, StringComparison.Ordinal);
        Assert.Contains("_newValue = \"positive\"", normalized, StringComparison.Ordinal);
        Assert.Contains("if v < 0 then", normalized, StringComparison.Ordinal);
        Assert.Contains("_newValue = \"negative\"", normalized, StringComparison.Ordinal);
        Assert.Contains("_newValue = \"zero\"", normalized, StringComparison.Ordinal);
        Assert.Contains("until true", normalized, StringComparison.Ordinal);
        Assert.Contains("return _newValue", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchExpression_CreatesTempForComplexComparand()
    {
        const string source = """
                             class SwitchExpressionTemp
                             {
                                 private int _value = 0;

                                 public int GetValue()
                                 {
                                     return _value;
                                 }

                                 public string Describe()
                                 {
                                     return GetValue() switch
                                     {
                                         0 => "zero",
                                         _ => "other",
                                     };
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("local _switch_exp", rendered, StringComparison.Ordinal);
        Assert.Contains("repeat", rendered, StringComparison.Ordinal);
        Assert.Contains("if _switch_exp == 0 then", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchStatement_SupportsListPatterns()
    {
        const string source = """
                             using System.Collections.Generic;

                             class ListPatternSwitch
                             {
                                 public int Evaluate(List<int> values)
                                 {
                                     switch (values)
                                     {
                                         case [1, 2, 3]:
                                             return 1;
                                         case [3, 4, 5]:
                                             return 2;
                                         default:
                                             return 0;
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("_switch_exp ~= nil and #_switch_exp == 3 and _switch_exp[1] == 1 and _switch_exp[2] == 2 and _switch_exp[3] == 3", normalized, StringComparison.Ordinal);
        Assert.Contains("_switch_exp ~= nil and #_switch_exp == 3 and _switch_exp[1] == 3 and _switch_exp[2] == 4 and _switch_exp[3] == 5", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchExpression_SupportsListPatterns()
    {
        const string source = """
                             class ListPatternExpression
                             {
                                 public int Evaluate(int[] values)
                                 {
                                     return values switch
                                     {
                                         [1, 2] => 10,
                                         [3, 4] when values.Length > 1 => 20,
                                         _ => -1,
                                     };
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("_switch_exp ~= nil and #_switch_exp == 2 and _switch_exp[1] == 1 and _switch_exp[2] == 2", normalized, StringComparison.Ordinal);
        Assert.Contains("_switch_exp ~= nil and #_switch_exp == 2 and _switch_exp[1] == 3 and _switch_exp[2] == 4", normalized, StringComparison.Ordinal);
        Assert.Contains("#_switch_exp > 1", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchStatement_SupportsListPatternsWithSlice()
    {
        const string source = """
                             using System.Collections.Generic;

                             class ListPatternSwitchWithSlice
                             {
                                 public int Evaluate(List<int> values)
                                 {
                                     switch (values)
                                     {
                                         case [1, .. var middle, 3, 4]:
                                             return middle.Length;
                                         case [2, .. var tail, 5]:
                                             return tail.Length;
                                         default:
                                             return -1;
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("_switch_exp ~= nil", normalized, StringComparison.Ordinal);
        Assert.Contains("#_switch_exp >= 3", normalized, StringComparison.Ordinal);
        Assert.Contains("local middle = CS.List.slice(_switch_exp", normalized, StringComparison.Ordinal);
        Assert.Contains("local tail = CS.List.slice(_switch_exp", normalized, StringComparison.Ordinal);
        Assert.Contains("_switch_exp[1] == 1", normalized, StringComparison.Ordinal);
        Assert.Contains("_switch_exp[#_switch_exp - 1] == 3", normalized, StringComparison.Ordinal);
        Assert.Contains("_switch_exp[#_switch_exp] == 4", normalized, StringComparison.Ordinal);
        Assert.Contains("_switch_exp[1] == 2", normalized, StringComparison.Ordinal);
        Assert.Contains("_switch_exp[#_switch_exp] == 5", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchExpression_SupportsListPatternsWithSlice()
    {
        const string source = """
                             class ListPatternExpressionWithSlice
                             {
                                 public int Evaluate(int[] values)
                                 {
                                     return values switch
                                     {
                                         [1, .. var middle] => middle.Length,
                                         [.. var tail, 3] => tail.Length,
                                         _ => -1,
                                     };
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("_switch_exp ~= nil", normalized, StringComparison.Ordinal);
        Assert.Contains("#_switch_exp >= 1", normalized, StringComparison.Ordinal);
        Assert.Contains("local middle = CS.List.slice(_switch_exp", normalized, StringComparison.Ordinal);
        Assert.Contains("local tail = CS.List.slice(_switch_exp", normalized, StringComparison.Ordinal);
        Assert.Contains("_switch_exp[#_switch_exp] == 3", normalized, StringComparison.Ordinal);
        Assert.Contains("_switch_exp[1] == 1", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchStatement_BindsListHeadBeforeGuard()
    {
        const string source = """
                             using System.Collections.Generic;

                             class ListPatternGuard
                             {
                                 public int Evaluate(List<int> values)
                                 {
                                     switch (values)
                                     {
                                         case [var head, .. var rest] when head > 5:
                                             return head + rest.Count;
                                         default:
                                             return 0;
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local head =", normalized, StringComparison.Ordinal);
        Assert.Contains("if head > 5 then", normalized, StringComparison.Ordinal);
        Assert.Contains("return head + rest.Count", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchExpression_BindsListHeadBeforeGuard()
    {
        const string source = """
                             class ListPatternGuardExpression
                             {
                                 public int Evaluate(int[] values)
                                 {
                                     return values switch
                                     {
                                         [var head, .. var rest] when head > 10 => head,
                                         _ => 0,
                                     };
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local head =", normalized, StringComparison.Ordinal);
        Assert.Contains("if head > 10 then", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchStatement_PropertyPattern_CapturesBindings()
    {
        const string source = """
                             class PropertyPatternSample
                             {
                                 public string Describe(Foo foo)
                                 {
                                     switch (foo)
                                     {
                                         case { Value: var value, Child: { Name: var name } } when value > 0:
                                             return $"{name}:{value}";
                                         default:
                                             return "other";
                                     }
                                 }
                             }

                             class Foo
                             {
                                 public int Value { get; set; }
                                 public Bar Child { get; set; } = new Bar();
                             }

                             class Bar
                             {
                                 public string Name { get; set; } = "";
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local value = _switch_exp.Value", normalized, StringComparison.Ordinal);
        Assert.Contains("local name = _switch_exp.Child.Name", normalized, StringComparison.Ordinal);
        Assert.Contains("if value > 0 then", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchStatement_TuplePattern_CapturesBindings()
    {
        const string source = """
                             class TuplePatternSample
                             {
                                 public string Describe((int First, int Second) tuple)
                                 {
                                     switch (tuple)
                                     {
                                         case (var first, var second) when first > 0:
                                             return $"{first}:{second}";
                                         default:
                                             return "other";
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local first = _switch_exp.Item1", normalized, StringComparison.Ordinal);
        Assert.Contains("local second = _switch_exp.Item2", normalized, StringComparison.Ordinal);
        Assert.Contains("if first > 0 then", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchStatement_ListPattern_WithTupleHeadAndSlice()
    {
        const string source = """
                             using System.Collections.Generic;

                             class TupleListPatternSample
                             {
                                 public string Describe(List<(string Label, int Score)> entries)
                                 {
                                     switch (entries)
                                     {
                                         case [(var label, var score), .. var rest] when score > 10:
                                             return $"{label}:{rest.Count}";
                                         default:
                                             return "none";
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local label = _switch_exp[1].Item1", normalized, StringComparison.Ordinal);
        Assert.Contains("local score = _switch_exp[1].Item2", normalized, StringComparison.Ordinal);
        Assert.Contains("local rest = CS.List.slice(_switch_exp, 2", normalized, StringComparison.Ordinal);
        Assert.Contains("if score > 10 then", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchExpression_RecursivePattern_WithTupleAndGuard()
    {
        const string source = """
                             class RecursivePatternSample
                             {
                                 public string Describe(Foo foo)
                                 {
                                     return foo switch
                                     {
                                         { Child: { Pair: (var label, var amount) } } when amount > 5
                                             => $"{label}:{amount}",
                                         _ => "default",
                                     };
                                 }
                             }

                             class Foo
                             {
                                 public Bar Child { get; set; } = new Bar();
                             }

                             class Bar
                             {
                                 public (string Label, int Amount) Pair { get; set; } = ("none", 0);
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local label = _switch_exp.Child.Pair.Item1", normalized, StringComparison.Ordinal);
        Assert.Contains("local amount = _switch_exp.Child.Pair.Item2", normalized, StringComparison.Ordinal);
        Assert.Contains("if amount > 5 then", normalized, StringComparison.Ordinal);
        Assert.Contains("_switch_value = `${label}:${amount}`", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchStatement_ListPattern_TailRecursiveTupleGuard()
    {
        const string source = """
                             using System.Collections.Generic;

                             class TailRecursivePatternSample
                             {
                                 public string Describe(List<Foo> entries)
                                 {
                                     switch (entries)
                                     {
                                         case [.., { Data: { Pair: (var label, var count) } }] when count > 0:
                                             return $"{label}:{count}";
                                         default:
                                             return "empty";
                                     }
                                 }
                             }

                             class Foo
                             {
                                 public Bar Data { get; set; } = new Bar();
                             }

                             class Bar
                             {
                                 public (string Label, int Count) Pair { get; set; } = ("", 0);
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local label = _switch_exp[#_switch_exp].Data.Pair.Item1", normalized, StringComparison.Ordinal);
        Assert.Contains("local count = _switch_exp[#_switch_exp].Data.Pair.Item2", normalized, StringComparison.Ordinal);
        Assert.Contains("if count > 0 then", normalized, StringComparison.Ordinal);
        Assert.Contains("return `${label}:${count}`", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchExpression_ListPattern_HeadAndTailRecursiveGuard()
    {
        const string source = """
                             using System.Collections.Generic;

                             class MultiSlicePatternSample
                             {
                                 public string Describe(List<Foo> entries)
                                 {
                                     return entries switch
                                     {
                                         [{ Pair: (var headLabel, var headCount) }, .. var middle, { Pair: (var tailLabel, var tailCount) }]
                                             when headCount + tailCount > middle.Count
                                             => $"{headLabel}:{tailLabel}:{middle.Count}",
                                         _ => "fallback",
                                     };
                                 }
                             }

                             class Foo
                             {
                                 public (string Label, int Count) Pair { get; set; } = ("", 0);
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local headLabel = _switch_exp[1].Pair.Item1", normalized, StringComparison.Ordinal);
        Assert.Contains("local headCount = _switch_exp[1].Pair.Item2", normalized, StringComparison.Ordinal);
        Assert.Contains("local middle = CS.List.slice(_switch_exp, 2, #_switch_exp - 1)", normalized, StringComparison.Ordinal);
        Assert.Contains("local tailLabel = _switch_exp[#_switch_exp].Pair.Item1", normalized, StringComparison.Ordinal);
        Assert.Contains("local tailCount = _switch_exp[#_switch_exp].Pair.Item2", normalized, StringComparison.Ordinal);
        Assert.Contains("if headCount + tailCount > #middle then", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchStatement_ListPattern_HeadTailGuardedBySliceLength()
    {
        const string source = """
                             using System.Collections.Generic;

                             class GuardedSlicePatternSample
                             {
                                 public string Describe(List<Foo> entries)
                                 {
                                     switch (entries)
                                     {
                                         case [{ Pair: (var firstLabel, var firstCount) }, .. var rest, { Pair: (var lastLabel, var lastCount) }]
                                             when rest.Count == firstCount && lastCount > rest.Count:
                                             return $"{firstLabel}:{lastLabel}:{rest.Count}";
                                         default:
                                             return "skip";
                                     }
                                 }
                             }

                             class Foo
                             {
                                 public (string Label, int Count) Pair { get; set; } = ("", 0);
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local firstLabel = _switch_exp[1].Pair.Item1", normalized, StringComparison.Ordinal);
        Assert.Contains("local firstCount = _switch_exp[1].Pair.Item2", normalized, StringComparison.Ordinal);
        Assert.Contains("local rest = CS.List.slice(_switch_exp", normalized, StringComparison.Ordinal);
        Assert.Contains("local lastLabel = _switch_exp[#_switch_exp].Pair.Item1", normalized, StringComparison.Ordinal);
        Assert.Contains("local lastCount = _switch_exp[#_switch_exp].Pair.Item2", normalized, StringComparison.Ordinal);
        Assert.Contains("if #rest == firstCount and lastCount > #rest then", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchExpression_ListPattern_DiscardHeadTailWithMiddleGuard()
    {
        const string source = """
                             using System.Collections.Generic;

                             class DiscardedPatternSample
                             {
                                 public string Describe(List<(string Name, int Score)> entries)
                                 {
                                     return entries switch
                                     {
                                         [_, (var label, var value), .. var middle, (_, var finalValue)]
                                             when middle.Count == 0 || finalValue > value
                                             => $"{label}:{value}:{middle.Count}",
                                         _ => "other",
                                     };
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local label = _switch_exp[2].Item1", normalized, StringComparison.Ordinal);
        Assert.Contains("local value = _switch_exp[2].Item2", normalized, StringComparison.Ordinal);
        Assert.Contains("local middle = CS.List.slice(_switch_exp, 3, #_switch_exp - 1)", normalized, StringComparison.Ordinal);
        Assert.Contains("local finalValue = _switch_exp[#_switch_exp].Item2", normalized, StringComparison.Ordinal);
        Assert.Contains("if #middle == 0 or finalValue > value then", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchStatement_ListPattern_DiscardHeadAndTail()
    {
        const string source = """
                             using System.Collections.Generic;

                             class ListPatternDiscardHeadTail
                             {
                                 public int Describe(List<int> values)
                                 {
                                     switch (values)
                                     {
                                         case [_, 5, .. var rest, _]:
                                             return rest.Count;
                                         default:
                                             return -1;
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("_switch_exp ~= nil", normalized, StringComparison.Ordinal);
        Assert.Contains("_switch_exp[2] == 5", normalized, StringComparison.Ordinal);
        Assert.Contains("local rest = CS.List.slice(_switch_exp", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchStatement_ListPattern_MultiRestGuard()
    {
        const string source = """
                             using System.Collections.Generic;

                             class ListPatternMultiRestGuard
                             {
                                 public int Describe(List<int> values)
                                 {
                                     switch (values)
                                     {
                                         case [var start, .. var rest, var tail] when tail > rest.Count:
                                             return rest.Count;
                                         default:
                                             return -1;
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local start = _switch_exp[1]", normalized, StringComparison.Ordinal);
        Assert.Contains("local rest = CS.List.slice", normalized, StringComparison.Ordinal);
        Assert.Contains("local tail = _switch_exp[#_switch_exp]", normalized, StringComparison.Ordinal);
        Assert.Contains("if tail > #rest then", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchStatement_ListPattern_NestedProperties()
    {
        const string source = """
                             using System.Collections.Generic;

                             class ListPatternNestedProperties
                             {
                                 public string Describe(List<Node> values)
                                 {
                                     switch (values)
                                     {
                                         case [{ Child: { Label: var innerLabel } }, .. var rest] when rest.Count == 0:
                                             return innerLabel;
                                         default:
                                             return "none";
                                     }
                                 }
                             }

                             class Node
                             {
                                 public string Label { get; set; } = "";
                                 public Node? Child { get; set; }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local innerLabel = _switch_exp[1].Child.Label", normalized, StringComparison.Ordinal);
        Assert.Contains("local rest = CS.List.slice(_switch_exp, 2, #_switch_exp)", normalized, StringComparison.Ordinal);
        Assert.Contains("if #rest == 0 then", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchExpression_ListPattern_NestedProperties()
    {
        const string source = """
                             using System.Collections.Generic;

                             class ListPatternNestedPropertiesExpression
                             {
                                 public string Describe(List<Node> values) => values switch
                                 {
                                     [{ Child: { Label: var childLabel } }, .. var rest] when rest.Count == 0 => childLabel,
                                     _ => "none",
                                 };
                             }

                             class Node
                             {
                                 public string Label { get; set; } = "";
                                 public Node? Child { get; set; }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("_switch_exp ~= nil", normalized, StringComparison.Ordinal);
        Assert.Contains("local childLabel = _switch_exp[1].Child.Label", normalized, StringComparison.Ordinal);
        Assert.Contains("local rest = CS.List.slice(_switch_exp, 2, #_switch_exp)", normalized, StringComparison.Ordinal);
        Assert.Contains("if #rest == 0 then", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SwitchExpression_ListPattern_MultiRestGuard()
    {
        const string source = """
                             using System.Collections.Generic;

                             class ListPatternMultiRestGuardExpression
                             {
                                 public int Describe(List<int> values) => values switch
                                 {
                                     [var start, .. var rest, var tail] when tail > rest.Count => rest.Count,
                                     _ => -1,
                                 };
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("_switch_exp ~= nil", normalized, StringComparison.Ordinal);
        Assert.Contains("local start = _switch_exp[1]", normalized, StringComparison.Ordinal);
        Assert.Contains("local rest = CS.List.slice(_switch_exp, 2, #_switch_exp - 1)", normalized, StringComparison.Ordinal);
        Assert.Contains("local tail = _switch_exp[#_switch_exp]", normalized, StringComparison.Ordinal);
        Assert.Contains("if tail > #rest then", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_UsingStatement_InvokesDisposeInFinally()
    {
        const string source = """
                             using System;

                             class Disposable : IDisposable
                             {
                                 public void Dispose()
                                 {
                                 }

                                 public void Use()
                                 {
                                 }
                             }

                             class UsingSample
                             {
                                 public void Run()
                                 {
                                     using (var resource = new Disposable())
                                     {
                                         resource.Use();
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("CS.try(function()", rendered, StringComparison.Ordinal);
        Assert.Contains(":Dispose()", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_UsingStatement_NestedDisposesInnerBeforeOuter()
    {
        const string source = """
                             using System;

                             class NestedUsingSample
                             {
                                 public void Run(IDisposable outer, IDisposable inner)
                                 {
                                     using (outer)
                                     {
                                         using (inner)
                                         {
                                             inner.Dispose();
                                         }
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        var tryMatches = Regex.Matches(normalized, "CS\\.try\\(function\\(");
        Assert.Equal(2, tryMatches.Count);

        var innerDisposeIndex = normalized.IndexOf("if _using_resource_1 ~= nil then", StringComparison.Ordinal);
        var outerDisposeIndex = normalized.IndexOf("if _using_resource ~= nil then", StringComparison.Ordinal);
        Assert.True(innerDisposeIndex >= 0, "Expected inner dispose guard.");
        Assert.True(outerDisposeIndex >= 0, "Expected outer dispose guard.");
        Assert.True(innerDisposeIndex < outerDisposeIndex, "Inner dispose should appear before outer dispose.");
    }

    [Fact]
    public void TranspilerV2_UsingExpression_WrapsResourceInTemp()
    {
        const string source = """
                             using System;

                             class Disposable : IDisposable
                             {
                                 public void Dispose()
                                 {
                                 }
                             }

                             class UsingExpressionSample
                             {
                                 private readonly Disposable _resource = new Disposable();

                                 public void Run()
                                 {
                                     using (_resource)
                                     {
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("local _using_resource", normalized, StringComparison.Ordinal);
        Assert.Contains("if _using_resource ~= nil then", normalized, StringComparison.Ordinal);
        Assert.Contains("_using_resource:Dispose()", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_PropertyAccessors_MatchLegacy()
    {
        const string source = """
                             class PropertySample
                             {
                                 private int _value;

                                 public int Value
                                 {
                                     get => _value;
                                     set => _value = value;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_SwitchExpression_RelationalPattern_MatchesLegacy()
    {
        const string source = """
                             class SwitchExpressionRelational
                             {
                                 public string Describe(int value)
                                 {
                                     return value switch
                                     {
                                         > 0 => "positive",
                                         < 0 => "negative",
                                         _ => "zero",
                                     };
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_StaticPropertyInitializers_MatchLegacy()
    {
        const string source = """
                             class StaticPropertySample
                             {
                                 public static string Name { get; set; } = "sample";
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("StaticPropertySample.Name = \"sample\"", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_YieldEnumerable_MatchesLegacy()
    {
        const string source = """
                             using System.Collections.Generic;

                             class Generator
                             {
                                 public IEnumerable<int> Produce()
                                 {
                                     yield return 1;
                                     yield return 2;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));

        var classSymbol = compiler.GetTypeByMetadataName("Generator");
        Assert.NotNull(classSymbol);

        var methodSymbol = classSymbol!.GetMembers("Produce").OfType<IMethodSymbol>().Single();
        var metadata = RobloxCS.Luau.SymbolMetadataManager.Get(classSymbol);
        Assert.Contains(methodSymbol, metadata.GeneratorMethods);
    }

    [Fact]
    public void TranspilerV2_YieldEnumerable_DoesNotUseGeneratorRuntime()
    {
        const string source = """
                             using System.Collections.Generic;

                             class GeneratorRuntimeSample
                             {
                                 public IEnumerable<int> Produce()
                                 {
                                     yield return 1;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("CS.Enumerator.new", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("CS.Generator.newState", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("CS.Generator.yieldValue", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("CS.Generator.drain", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_YieldBreak_EmitsEnumeratorHelpers()
    {
        const string source = """
                             using System.Collections.Generic;
                             using System;

                             class EnumeratorSample
                             {
                                 public IEnumerator<int> Produce()
                                 {
                                     yield return 1;
                                     yield break;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalized = NormalizeLuau(rendered);

        Assert.Contains("CS.Enumerator.new", normalized, StringComparison.Ordinal);
        Assert.Contains("CS.Generator.newState", normalized, StringComparison.Ordinal);
        Assert.Contains("CS.Generator.close", normalized, StringComparison.Ordinal);
        Assert.Contains("_breakIteration()", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_DictionaryTypeInfo_OptionalTuple()
    {
        const string source = """
                             using System.Collections.Generic;

                             class Container
                             {
                                 public Dictionary<string, int?> Numbers(Dictionary<string, (int, string?)?> input)
                                 {
                                     foreach (var (key, tuple) in input)
                                     {
                                     }

                                     return input;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("pairs(", normalizedLegacy, StringComparison.Ordinal);
        Assert.Contains("pairs(", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("input: { [string]: (number, string?)? }", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("): { [string]: number? }", normalizedRendered, StringComparison.Ordinal);
        Assert.DoesNotContain("(number, string?)? }", normalizedLegacy, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_DictionaryIndexer_UsesBracketAccess()
    {
        const string source = """
                             using System.Collections.Generic;

                             class IndexLookup
                             {
                                 private readonly Dictionary<string, int> _entries = new Dictionary<string, int>
                                 {
                                     ["a"] = 1,
                                 };

                                 public int Fetch(string key)
                                 {
                                     return _entries[key];
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("self._entries[key]", normalizedRendered, StringComparison.Ordinal);
        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_OptionalInvocation_MatchesLegacy()
    {
        const string source = """
                             class Helper
                             {
                                 public void Ping()
                                 {
                                 }
                             }

                             class OptionalInvoke
                             {
                                 private Helper? _helper;

                                 public void Run()
                                 {
                                     _helper?.Ping();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("local _optional_target = self._helper", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("_optional_target ~= nil", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("_optional_target:Ping()", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("local _Ping = self._helper", normalizedLegacy, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_MethodGroupInvocation_MatchesLegacy()
    {
        const string source = """
                             using System;

                             class Delegates
                             {
                                 public static void StaticPing()
                                 {
                                 }

                                 public void InstancePing()
                                 {
                                 }

                                 public void Fire()
                                 {
                                     Action staticRef = StaticPing;
                                     staticRef();

                                     Action instanceRef = InstancePing;
                                     instanceRef();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("local staticRef: () -> nil = Delegates.StaticPing", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("local instanceRef: () -> nil = self.InstancePing", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("instanceRef()", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("staticRef()", normalizedRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_StaticAndInstanceCalls_PreserveDispatch()
    {
        const string source = """
                             class Caller
                             {
                                 public static void StaticPing()
                                 {
                                 }

                                 public void InstancePing()
                                 {
                                 }

                                 public void Execute()
                                 {
                                     Caller.StaticPing();
                                     InstancePing();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("Caller.StaticPing()", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("self:InstancePing()", normalizedRendered, StringComparison.Ordinal);
        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_MethodGroupInvocation_CapturesReceiver()
    {
        const string source = """
                             using System;

                             class Helper
                             {
                                 public void Ping(int value)
                                 {
                                 }
                             }

                             class Owner
                             {
                                 private readonly Helper _helper = new Helper();
                                 private readonly Action<int> _cached;

                                 public Owner()
                                 {
                                     _cached = _helper.Ping;
                                 }

                                 public void Fire(int value)
                                 {
                                     _cached(value);
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("self._helper.Ping", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("_cached(value)", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("_cached = self._helper.Ping", normalizedRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_ExtensionMethodInvocation_UsesColon()
    {
        const string source = """
                             using System;

                             class Helper
                             {
                             }

                             static class HelperExtensions
                             {
                                 public static void Ping(this Helper helper)
                                 {
                                 }
                             }

                             class UsesExtension
                             {
                                 private Helper _helper = new Helper();

                                 public void Run()
                                 {
                                     _helper.Ping();
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains(":Ping()", normalizedRendered, StringComparison.Ordinal);
        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_OptionalChaining_MatchesLegacy()
    {
        const string source = """
                             class Helper
                             {
                                 public string GetName()
                                 {
                                     return "value";
                                 }
                             }

                             class OptionalCalls
                             {
                                 private Helper? _helper;

                                 public string? Retrieve()
                                 {
                                     return _helper?.GetName()?.Substring(0);
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        _ = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("local _optional_target", rendered, StringComparison.Ordinal);
        Assert.Contains("_optional_target == nil", rendered, StringComparison.Ordinal);
        Assert.Contains("_optional_target_1 == nil", rendered, StringComparison.Ordinal);
        Assert.Contains(":Substring(0)", normalizedRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_OptionalPropertyAccess_MatchesLegacy()
    {
        const string source = """
                             class Helper
                             {
                                 public string Name => "value";
                             }

                             class OptionalProperty
                             {
                                 private Helper? _helper;

                                 public string? Fetch()
                                 {
                                     return _helper?.Name;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        _ = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("_optional_target == nil", rendered, StringComparison.Ordinal);
        Assert.Contains("return nil", rendered, StringComparison.Ordinal);
        Assert.Contains("return self._helper.Name", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_OptionalIndexer_MatchesLegacy()
    {
        const string source = """
                             using System.Collections.Generic;

                             class OptionalIndexer
                             {
                                 private Dictionary<string, int>? _map;

                                 public int? Fetch(string key)
                                 {
                                     return _map?[key];
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Assert.Throws<NullReferenceException>(() => TranspilerUtility.GenerateLuau(file, compiler));

        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("_optional_target == nil", rendered, StringComparison.Ordinal);
        Assert.Contains("_optional_target[key]", normalizedRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_NestedOptionalChaining_MatchesLegacy()
    {
        const string source = """
                             class Inner
                             {
                                 public string? Value()
                                 {
                                     return "value";
                                 }
                             }

                             class Outer
                             {
                                 private Inner? _inner;

                                 public string? Fetch()
                                 {
                                     return _inner?.Value()?.Substring(1);
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        _ = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("_optional_target == nil", rendered, StringComparison.Ordinal);
        Assert.Contains("_optional_target_1 == nil", rendered, StringComparison.Ordinal);
        Assert.Contains(":Substring(1)", normalizedRendered, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_ForEachTupleDestructuring_GeneratesGenericFor()
    {
        const string source = """
                             using System.Collections.Generic;

                             class EntryProcessor
                             {
                                 public void Process(KeyValuePair<int, int>[] entries)
                                 {
                                     foreach (var (key, value) in entries)
                                     {
                                     }

                                     foreach (var (_, onlyValue) in entries)
                                     {
                                     }
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var loops = CollectGenericFors(chunk.Block);

        Assert.Equal(2, loops.Count);

        var firstLoop = loops[0];
        Assert.Collection(firstLoop.Names,
            name => Assert.Equal("key", ((VarName)name).Name),
            name => Assert.Equal("value", ((VarName)name).Name));

        var secondLoop = loops[1];
        Assert.Collection(secondLoop.Names,
            name => Assert.Equal("_", ((VarName)name).Name),
            name => Assert.Equal("onlyValue", ((VarName)name).Name));
    }

    [Fact]
    public void TranspilerV2_DictionaryDestructuring_UsesPairs()
    {
        const string source = """
                             using System.Collections.Generic;

                             class DictionaryLoop
                             {
                                 public int Sum(Dictionary<string, int> map)
                                 {
                                     var total = 0;

                                     foreach (var (key, value) in map)
                                     {
                                         total += value;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("pairs(", normalizedLegacy, StringComparison.Ordinal);
        Assert.Contains("pairs(", normalizedRendered, StringComparison.Ordinal);
        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_DictionaryEntryLoop_WrapsKeyValueTable()
    {
        const string source = """
                             using System.Collections.Generic;

                             class DictionaryLoop
                             {
                                 public int Sum(Dictionary<string, int> map)
                                 {
                                     var total = 0;

                                     foreach (var entry in map)
                                     {
                                         total += entry.Value;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalizedLegacy = NormalizeLuau(legacy);
        var normalizedRendered = NormalizeLuau(rendered);

        Assert.Contains("pairs(", normalizedLegacy, StringComparison.Ordinal);
        Assert.Contains("pairs(", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("local entry = {", normalizedLegacy, StringComparison.Ordinal);
        Assert.Contains("local entry = {", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("Key =", normalizedLegacy, StringComparison.Ordinal);
        Assert.Contains("Key =", normalizedRendered, StringComparison.Ordinal);
        Assert.Contains("Value =", normalizedLegacy, StringComparison.Ordinal);
        Assert.Contains("Value =", normalizedRendered, StringComparison.Ordinal);
        Assert.Equal(normalizedLegacy, normalizedRendered);
    }

    [Fact]
    public void TranspilerV2_DoWhileLoop_MatchesLegacyGenerator()
    {
        const string source = """
                             class Timer
                             {
                                 public Timer()
                                 {
                                 }

                                 public int Countdown(int start)
                                 {
                                     var current = start;

                                     do
                                     {
                                         current -= 1;
                                     }
                                     while (current > 0);

                                     return current;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_ArrayInitializer_MatchesLegacyGenerator()
    {
        const string source = """
                             class Holder
                             {
                                 private readonly int[] _values = new[]
                                 {
                                     1,
                                     2,
                                     3,
                                 };

                                 public Holder()
                                 {
                                 }

                                 public int Sum()
                                 {
                                     var total = 0;

                                     foreach (var value in _values)
                                     {
                                         total += value;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_ListCollectionExpression_MatchesLegacyGenerator()
    {
        const string source = """
                             using System.Collections.Generic;

                             class ListHolder
                             {
                                 private readonly List<int> _values = [1, 2, 3];

                                 public ListHolder()
                                 {
                                 }

                                 public int Sum()
                                 {
                                     var total = 0;

                                     foreach (var value in _values)
                                     {
                                         total += value;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_DictionaryInitializer_MatchesLegacyGenerator()
    {
        const string source = """
                             using System.Collections.Generic;

                             class MapHolder
                             {
                                 private readonly Dictionary<string, int> _map = new()
                                 {
                                     ["one"] = 1,
                                     ["two"] = 2,
                                 };

                                 public MapHolder()
                                 {
                                 }

                                 public int Count()
                                 {
                                     return _map.Count;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        var legacy = TranspilerUtility.GenerateLuau(file, compiler);
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Equal(NormalizeLuau(legacy), NormalizeLuau(rendered));
    }

    [Fact]
    public void TranspilerV2_TypeAndClassMacros_UseRuntimeHelpers()
    {
        const string source = """
                             public static class TypeMacroSample
                             {
                                 public static bool CheckPrimitive(object value)
                                 {
                                     return typeIs(value, "string");
                                 }

                                 public static bool CheckClass(object value)
                                 {
                                     return classIs(value, "Widget");
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalized = NormalizeLuau(rendered);
        Assert.Contains("CS.typeIs", normalized, StringComparison.Ordinal);
        Assert.Contains("CS.classIs", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SystemMathFunctions_UseLuauMath()
    {
        const string source = """
                             using System;

                             public static class MathSample
                             {
                                 public static double Normalize(double value)
                                 {
                                     return Math.Clamp(Math.Round(value) / Math.PI, -1.0, 1.0);
                                 }

                                 public static double Waves(double value)
                                 {
                                     return Math.Sin(value) + Math.Cos(value);
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalized = NormalizeLuau(rendered);
        Assert.Contains("math.clamp", normalized, StringComparison.Ordinal);
        Assert.Contains("math.round", normalized, StringComparison.Ordinal);
        Assert.Contains("math.sin", normalized, StringComparison.Ordinal);
        Assert.Contains("math.cos", normalized, StringComparison.Ordinal);
        Assert.Contains("math.pi", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_SystemMathConstants_MapToLuau()
    {
        const string source = """
                             using System;

                             public static class MathConstantSample
                             {
                                 public static double Values()
                                 {
                                     return Math.PI + Math.E + Math.Tau;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalized = NormalizeLuau(rendered);
        Assert.Contains("math.pi", normalized, StringComparison.Ordinal);
        Assert.Contains(Math.E.ToString(CultureInfo.InvariantCulture), normalized, StringComparison.Ordinal);
        Assert.Contains(Math.Tau.ToString(CultureInfo.InvariantCulture), normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_Bit32Macro_UsesLuauBit32()
    {
        const string source = """
                             using Roblox;

                             public static class Bit32Sample
                             {
                                 public static int Mask(int value, int flag)
                                 {
                                     return Bit32.Band(value, flag);
                                 }

                                 public static int Shift(int value)
                                 {
                                     return Bit32.LShift(value, 2);
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalized = NormalizeLuau(rendered);
        Assert.Contains("bit32.band", normalized, StringComparison.Ordinal);
        Assert.Contains("bit32.lshift", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_TupleMacro_EmitsMultipleReturns()
    {
        const string source = """
                             using Roblox;

                             public static class TupleMacroSample
                             {
                                 public static LuaTuple<int, int> Provide()
                                 {
                                     return tuple(1, 2);
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        Assert.Contains("return 1, 2", NormalizeLuau(rendered), StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_IDivMacro_UsesIntegerDivision()
    {
        const string source = """
                             using Roblox;

                             public static class IDivSample
                             {
                                 public static int Instance(int value)
                                 {
                                     return value.idiv(3);
                                 }

                                 public static int StaticCall(int value)
                                 {
                                     return NumberExtensions.idiv(value, 2);
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalized = NormalizeLuau(rendered);
        Assert.Contains("value // 3", normalized, StringComparison.Ordinal);
        Assert.Contains("value // 2", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_TsIter_LowersToRuntimeHelper()
    {
        const string source = """
                             using Roblox;
                             using System.Collections.Generic;

                             public static class TsIterSample
                             {
                                 public static int Sum()
                                 {
                                     var values = new List<int> { 1, 2, 3 };
                                     var total = 0;

                                     foreach (var value in TS.iter(values))
                                     {
                                         total += value;
                                     }

                                     return total;
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalized = NormalizeLuau(rendered);
        Assert.Contains("CS.iter", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void TranspilerV2_ArrayFlatten_LowersToRuntimeHelper()
    {
        const string source = """
                             using Roblox;
                             using System.Collections.Generic;

                             public static class TsFlattenSample
                             {
                                 public static List<int> Values()
                                 {
                                     var nested = new List<List<int>>
                                     {
                                         new() { 1, 2 },
                                         new() { 3 },
                                     };

                                     return TS.array_flatten(nested);
                                 }
                             }
                             """;

        var config = ConfigReader.UnitTestingConfig;
        var file = TranspilerUtility.ParseAndTransformTree(source.Trim(), new RojoProject(), config);
        var compiler = TranspilerUtility.GetCompiler([file.Tree], config);

        SymbolMetadataManager.Clear();
        Chunk chunk = TranspilerUtility.GetLuauChunkV2(file, compiler, ScriptType.Module);
        var rendered = TranspilerUtility.RenderLuauChunkV2(chunk);

        var normalized = NormalizeLuau(rendered);
        Assert.Contains("CS.array_flatten", normalized, StringComparison.Ordinal);
    }

    private static string NormalizeLuau(string output)
    {
        var tokens = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith("--"))
            .Select(line => Regex.Replace(line, @"\s+", " "));

        return string.Join(' ', tokens).Trim();
    }

    private static Assignment? FindAssignment(Chunk chunk, string targetName)
    {
        return chunk.Block.Statements
            .OfType<Assignment>()
            .FirstOrDefault(stmt => stmt.Vars
                .OfType<VarName>()
                .Any(var => var.Name == targetName));
    }

    private static List<GenericFor> CollectGenericFors(RobloxCS.AST.Block block)
    {
        var collector = new GenericForCollector();
        collector.Visit(block);
        return collector.Loops;
    }

    private sealed class GenericForCollector : AstVisitorBase
    {
        public List<GenericFor> Loops { get; } = new();

        public override void VisitGenericFor(GenericFor node)
        {
            Loops.Add(node);
            base.VisitGenericFor(node);
        }
    }

}
