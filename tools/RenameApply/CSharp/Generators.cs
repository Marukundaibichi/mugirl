using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RenameApply.CSharp;

// Runs the projects' source generators ([GeneratedRegex] and friends) so that the compilations match the real build.
// Analyzers proper are not run: the real build (dotnet build) checks them after the tool.
public static class Generators
{
    private sealed class Loader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath) => AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }

    private static readonly Loader SharedLoader = new();
    private static readonly Dictionary<string, List<ISourceGenerator>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static List<ISourceGenerator> For(IEnumerable<string> analyzerPaths, TextWriter log)
    {
        var result = new List<ISourceGenerator>();
        foreach (string path in analyzerPaths)
        {
            if (!Cache.TryGetValue(path, out List<ISourceGenerator>? gens))
            {
                gens = new List<ISourceGenerator>();
                try
                {
                    var reference = new AnalyzerFileReference(path, SharedLoader);
                    gens.AddRange(reference.GetGenerators(LanguageNames.CSharp));
                }
                catch (Exception ex) when (ex is FileLoadException or BadImageFormatException or TypeLoadException or ReflectionTypeLoadException or FileNotFoundException)
                {
                    log.WriteLine($"  (skipping analyzer {Path.GetFileName(path)}: {ex.GetType().Name})");
                }
                Cache[path] = gens;
            }
            result.AddRange(gens);
        }
        return result;
    }

    public static CSharpCompilation Run(CSharpCompilation c, ProjectModel p, TextWriter log)
    {
        var gens = For(p.AnalyzerPaths, log);
        if (gens.Count == 0)
        {
            return c;
        }
        GeneratorDriver driver = CSharpGeneratorDriver.Create(gens, parseOptions: p.ParseOptions);
        driver.RunGeneratorsAndUpdateCompilation(c, out Compilation updated, out _);
        return (CSharpCompilation)updated;
    }
}
