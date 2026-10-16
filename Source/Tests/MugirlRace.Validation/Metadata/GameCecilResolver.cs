using Mono.Cecil;

namespace MugirlRace.Validation.Metadata;

/// <summary>
/// 只读元数据用的 Cecil 程序集解析器：游戏目录、Harmony 3、依赖 Mod 与当前 .NET 运行时，并做与游戏运行时相同的 Unity
/// 名称重定向（1.6 构建的程序集引用 <c>UnityEngine.*</c> 模块，它们在 1.7 只以 UnityStubs 存在）。
/// </summary>
internal sealed class GameCecilResolver : DefaultAssemblyResolver
{
    public GameCecilResolver(IEnumerable<string> directories)
    {
        foreach (string directory in GetSearchDirectories())
        {
            RemoveSearchDirectory(directory);
        }

        foreach (string directory in directories.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddSearchDirectory(directory);
        }
    }

    public override AssemblyDefinition Resolve(AssemblyNameReference name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Name.StartsWith("UnityEngine", StringComparison.Ordinal)
            || (name.Name.StartsWith("Unity.", StringComparison.Ordinal) && name.Name != "Unity.Newtonsoft.Json")
            || name.Name == "Assembly-CSharp-firstpass")
        {
            return base.Resolve(new AssemblyNameReference("UnityStubs", new Version(0, 0)));
        }

        return base.Resolve(name);
    }
}
