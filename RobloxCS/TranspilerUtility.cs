using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RobloxCS.AST;
using RobloxCS.Luau;
using RobloxCS.Renderer;
using RobloxCS.Shared;
using RobloxCS.Transformers;
using RobloxCS.Transformers.Plugins;
using RobloxCS.TranspilerV2;
using Path = System.IO.Path;
using LuauAST = RobloxCS.Luau.AST;

namespace RobloxCS;

using TransformMethod = Func<FileCompilation, SyntaxTree>;

public static class TranspilerUtility
{
    public static string GenerateLuau(FileCompilation file, CSharpCompilation compiler)
    {
        var luauAST = GetLuauAST(file, compiler);
        var luau = new LuauWriter();

        return luau.Render(luauAST);
    }

    public static LuauAST GetLuauAST(FileCompilation file, CSharpCompilation compiler)
    {
        var analyzer = new Analyzer(file, compiler);
        var analysisResult = analyzer.Analyze(file.Tree.GetRoot());
        var generator = new LuauGenerator(file, compiler, analysisResult);

        return generator.GetLuauAST();
    }

    public static Chunk GetLuauChunkV2(FileCompilation file, CSharpCompilation compiler, ScriptType scriptType = ScriptType.Module)
    {
        var root = (CompilationUnitSyntax)file.Tree.GetRoot();
        var macroOptions = file.Config.Macro ?? new MacroOptions();
        var options = new TranspilerOptions(scriptType, macroOptions, file.RojoProject);
        SymbolMetadataManager.Clear();
        var context = new TranspilationContext(options, compiler, root);
        var transpiler = new CSharpTranspiler(context);

        return transpiler.Transpile();
    }

    public static string RenderLuauChunkV2(Chunk chunk)
    {
        var renderer = new RendererWalker();
        return renderer.Render(chunk);
    }

    public static CSharpCompilation GetCompiler(IEnumerable<SyntaxTree> trees, ConfigData config)
    {
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        
        return CSharpCompilation.Create("RobloxGame", // probably temporary until i set up msbuild (hell)
                                        trees,
                                        FileUtility.GetCompilationReferences(),
                                        compilationOptions);
    }

    // didn't change `source` to `path` cuz this is used in tests. this needs a big refactor
    public static FileCompilation ParseAndTransformTree(
        string source,
        RojoProject? rojoProject,
        ConfigData config,
        string path = "TestFile.cs",
        IReadOnlyList<TransformMethod>? transformers = null,
        string? projectDirectory = null)
    {
        var tree = ParseTree(source, path);
        var resolvedProjectDirectory = projectDirectory ?? config.ProjectRoot ?? Directory.GetCurrentDirectory();
        var file = GetFileCompilation(tree, rojoProject, config, resolvedProjectDirectory);
        var pipeline = transformers ?? CreateTransformerPipeline(config, resolvedProjectDirectory);

        ApplyTransformers(file, pipeline);
        return file;
    }

    private static FileCompilation GetFileCompilation(SyntaxTree tree, RojoProject? rojoProject, ConfigData config, string projectDirectory) =>
        new()
        {
            Tree = tree,
            RojoProject = rojoProject,
            Config = config,
            ProjectDirectory = projectDirectory
        };

    internal static IReadOnlyList<TransformMethod> CreateTransformerPipeline(ConfigData config, string projectDirectory)
    {
        var registrations = TransformerPluginLoader.Load(config, projectDirectory);

        var before = new List<TransformMethod>();
        var after = new List<TransformMethod>();

        foreach (var registration in registrations)
        {
            switch (registration.Phase)
            {
                case TransformerPhase.BeforeBuiltIn:
                    before.Add(registration.Transform);
                    break;
                case TransformerPhase.AfterBuiltIn:
                    after.Add(registration.Transform);
                    break;
            }
        }

        var pipeline = new List<TransformMethod>(before.Count + after.Count + 2);
        pipeline.AddRange(before);
        if (config.EnableUnityAliases)
        {
            pipeline.Add(BuiltInTransformers.UnityAliases());
        }
        pipeline.Add(BuiltInTransformers.Main());
        pipeline.AddRange(after);

        if (pipeline.Count == 0)
        {
            if (config.EnableUnityAliases)
            {
                pipeline.Add(BuiltInTransformers.UnityAliases());
            }

            pipeline.Add(BuiltInTransformers.Main());
        }

        return pipeline;
    }

    private static void ApplyTransformers(FileCompilation file, IReadOnlyList<TransformMethod> transformMethods)
    {
        foreach (var transform in transformMethods)
        {
            file.Tree = transform(file);
        }
    }

    private static SyntaxTree ParseTree(string source, string sourceFile)
    {
        var cleanTree = CSharpSyntaxTree.ParseText(source);
        var compilationUnit = (CompilationUnitSyntax)cleanTree.GetRoot();
        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System"));
        var newRoot = compilationUnit.AddUsings(usingDirective);

        return cleanTree
               .WithRootAndOptions(newRoot, cleanTree.Options)
               .WithFilePath(sourceFile);
    }
}
