using Mono.Cecil;

namespace MugirlRace.Validation.Metadata;

/// <summary>
/// 被测 Mod 与游戏程序集的纯元数据视图（Mono.Cecil）。这里读取的东西都不加载进进程，所以同名的其他构建（例如一致性比较的
/// 另一方）可以并排读取。
/// </summary>
internal sealed class MetadataWorld : IDisposable
{
    private readonly GameCecilResolver resolver;
    private readonly List<ModuleDefinition> owned = [];

    public MetadataWorld(string modAssembly, string? modPdb, IEnumerable<string> searchDirectories)
    {
        resolver = new GameCecilResolver(searchDirectories);
        Mod = Read(modAssembly);
        Sources = SourceMap.Read(modPdb);
    }

    /// <summary>被测的 1.7 MugirlRace.dll。</summary>
    public ModuleDefinition Mod { get; }

    /// <summary>被测程序集方法到源码文件的映射（来自便携式 PDB；没有 PDB 时为空）。</summary>
    public SourceMap Sources { get; }

    /// <summary>按名称解析被引用的程序集（游戏、引擎、Harmony、BCL）。</summary>
    public AssemblyDefinition? TryResolve(string assemblyName)
    {
        try
        {
            return resolver.Resolve(new AssemblyNameReference(assemblyName, new Version(0, 0)));
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    /// <summary>把程序集文件读入内存（不锁文件），引用经游戏目录解析。</summary>
    public ModuleDefinition Read(string path)
    {
        ModuleDefinition module = ModuleDefinition.ReadModule(path, new ReaderParameters
        {
            AssemblyResolver = resolver,
            InMemory = true,
            ReadingMode = ReadingMode.Deferred,
        });
        owned.Add(module);
        return module;
    }

    public void Dispose()
    {
        foreach (ModuleDefinition module in owned)
        {
            module.Dispose();
        }

        resolver.Dispose();
    }
}
