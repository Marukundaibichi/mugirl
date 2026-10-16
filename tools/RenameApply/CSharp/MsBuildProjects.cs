using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RenameApply.CSharp;

// One C# project as the compiler sees it: the exact csc command line of a design-time "Compile" run
// (SkipCompilerExecution + ProvideCommandLineArgs), so sources (incl. obj/ generated files), references, defines,
// nullable, warn-as-error and nowarn match the real build. Project references are replaced by in-memory compilations.
public sealed class ProjectModel
{
    public required string ProjectPath { get; init; }      // full path of the .csproj
    public required string Name { get; init; }              // file name without extension
    public required string Directory { get; init; }         // full path, trailing separator
    public required string AssemblyName { get; init; }
    public required List<string> SourceFiles { get; init; } // full paths, compiler order
    public required CSharpParseOptions ParseOptions { get; init; }
    public required CSharpCompilationOptions CompilationOptions { get; init; }
    public required List<(string Path, MetadataReferenceProperties Props)> MetadataRefs { get; init; }
    public required List<(string ProjectPath, MetadataReferenceProperties Props)> ProjectRefs { get; init; }
    public required List<string> AnalyzerPaths { get; init; }     // source generators are run (e.g. [GeneratedRegex])
    public List<string> ResolverDirectories { get; init; } = new(); // 包括项目引用的实际输出，供 emitted DLL/PDB 读回使用
}

public static class MsBuildProjects
{
    public static List<ProjectModel> Load(string sourceRoot, IReadOnlyList<string> msbuildProperties, TextWriter log)
    {
        var csprojs = System.IO.Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !RepoPaths.HasSegment(p.Replace('\\', '/'), "bin") && !RepoPaths.HasSegment(p.Replace('\\', '/'), "obj"))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var models = new List<ProjectModel>();
        foreach (string csproj in csprojs)
        {
            log.WriteLine($"  msbuild design-time: {Path.GetFileName(csproj)}");
            models.Add(LoadOne(csproj, msbuildProperties));
        }
        return TopologicalOrder(models);
    }

    private static ProjectModel LoadOne(string csproj, IReadOnlyList<string> msbuildProperties)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (string a in new[]
                 {
                     "msbuild", csproj, "-restore", "-nologo", "-v:q", "-nodeReuse:false", "-t:Compile",
                     "-p:SkipCompilerExecution=true", "-p:ProvideCommandLineArgs=true", "-p:BuildProjectReferences=false",
                     // A missing CoreCompile input makes the target run even when obj/ is up to date (what IDE design-time builds do).
                     @"-p:NonExistentFile=__NonExistentSubDir__\__NonExistentFile__",
                     "-getItem:CscCommandLineArgs", "-getItem:ReferencePathWithRefAssemblies", "-getItem:ReferencePath",
                     "-getProperty:AssemblyName", "-getProperty:MSBuildProjectDirectory",
                 })
        {
            psi.ArgumentList.Add(a);
        }
        foreach (string p in msbuildProperties)
        {
            psi.ArgumentList.Add("-p:" + p);
        }
        psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        using Process proc = Process.Start(psi)!;
        Task<string> err = proc.StandardError.ReadToEndAsync();
        string stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        int start = stdout.IndexOf('{');
        if (proc.ExitCode != 0 || start < 0)
        {
            throw new ToolException($"design-time build of {csproj} failed (exit {proc.ExitCode}):\n{stdout}\n{err.Result}");
        }
        using JsonDocument doc = JsonDocument.Parse(stdout.Substring(start));
        JsonElement props = doc.RootElement.GetProperty("Properties");
        JsonElement items = doc.RootElement.GetProperty("Items");
        string dir = props.GetProperty("MSBuildProjectDirectory").GetString()!;
        string assemblyName = props.GetProperty("AssemblyName").GetString()!;
        var args = items.GetProperty("CscCommandLineArgs").EnumerateArray().Select(e => e.GetProperty("Identity").GetString()!).ToList();
        if (args.Count == 0)
        {
            throw new ToolException($"design-time build of {csproj} produced no csc command line");
        }

        // Reference path -> source project, for every item that came from a ProjectReference.
        var projectRefByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string itemName in new[] { "ReferencePathWithRefAssemblies", "ReferencePath" })
        {
            if (!items.TryGetProperty(itemName, out JsonElement arr))
            {
                continue;
            }
            foreach (JsonElement it in arr.EnumerateArray())
            {
                if (it.TryGetProperty("ReferenceSourceTarget", out JsonElement rst) && rst.GetString() == "ProjectReference"
                    && it.TryGetProperty("MSBuildSourceProjectFile", out JsonElement src) && !string.IsNullOrEmpty(src.GetString()))
                {
                    projectRefByPath[Path.GetFullPath(it.GetProperty("Identity").GetString()!)] = Path.GetFullPath(src.GetString()!);
                    if (it.TryGetProperty("ReferenceAssembly", out JsonElement ra) && !string.IsNullOrEmpty(ra.GetString()))
                    {
                        projectRefByPath[Path.GetFullPath(ra.GetString()!)] = Path.GetFullPath(src.GetString()!);
                    }
                }
            }
        }

        CSharpCommandLineArguments parsed = CSharpCommandLineParser.Default.Parse(args, dir, sdkDirectory: null);
        var errors = parsed.Errors.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            throw new ToolException($"cannot parse csc arguments of {csproj}: {string.Join("; ", errors)}");
        }
        var metadataRefs = new List<(string, MetadataReferenceProperties)>();
        var projectRefs = new List<(string, MetadataReferenceProperties)>();
        foreach (CommandLineReference r in parsed.MetadataReferences)
        {
            string full = Path.GetFullPath(Path.Combine(dir, r.Reference));
            if (projectRefByPath.TryGetValue(full, out string? proj))
            {
                projectRefs.Add((proj, r.Properties));
            }
            else
            {
                metadataRefs.Add((full, r.Properties));
            }
        }
        // Doc comments are parsed so that crefs bind and unused usings (CS8019) are reported.
        CSharpParseOptions parseOptions = parsed.ParseOptions.WithDocumentationMode(DocumentationMode.Parse);
        return new ProjectModel
        {
            ProjectPath = Path.GetFullPath(csproj),
            Name = Path.GetFileNameWithoutExtension(csproj),
            Directory = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar,
            AssemblyName = assemblyName,
            SourceFiles = parsed.SourceFiles.Select(f => Path.GetFullPath(f.Path)).ToList(),
            ParseOptions = parseOptions,
            CompilationOptions = parsed.CompilationOptions,
            MetadataRefs = metadataRefs,
            ProjectRefs = projectRefs,
            AnalyzerPaths = parsed.AnalyzerReferences.Select(a => Path.GetFullPath(Path.Combine(dir, a.FilePath))).ToList(),
            ResolverDirectories = parsed.MetadataReferences.Select(r => Path.GetDirectoryName(Path.GetFullPath(Path.Combine(dir, r.Reference)))!)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    private static List<ProjectModel> TopologicalOrder(List<ProjectModel> models)
    {
        var byPath = models.ToDictionary(m => m.ProjectPath, StringComparer.OrdinalIgnoreCase);
        var result = new List<ProjectModel>();
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Visit(ProjectModel m)
        {
            if (state.TryGetValue(m.ProjectPath, out int s))
            {
                if (s == 1)
                {
                    throw new ToolException($"project reference cycle at {m.ProjectPath}");
                }
                return;
            }
            state[m.ProjectPath] = 1;
            foreach (var (p, _) in m.ProjectRefs)
            {
                if (!byPath.TryGetValue(p, out ProjectModel? dep))
                {
                    throw new ToolException($"{m.Name} references {p}, which is not a project under Source/");
                }
                Visit(dep);
            }
            state[m.ProjectPath] = 2;
            result.Add(m);
        }
        foreach (ProjectModel m in models)
        {
            Visit(m);
        }
        return result;
    }
}
