using System.Reflection;

namespace Mugirl.GameValidation.Core;

/// <summary>本进程中被测的雪牛娘构建：加载了名为 MugirlRace 的程序集的那个运行中 Mod。</summary>
/// <remarks>
/// <para>
/// 被测构建按程序集名识别，packageId 可配置：正式运行是开发目录的 HAR.MugirlRace，制作存档夹具时是带标记的临时旧构建
/// （packageId <c>mugirl.legacy.&lt;id&gt;</c>，由 tools/New-SaveFixture.ps1 布置）。参数文件给出期望的 packageId
/// （<see cref="ValidationOptions.TargetPackageId"/>）。
/// </para>
/// <para>
/// 只用 Verse 与反射类型描述被测 Mod，不引用 MugirlRace 的类型：-Plumbing 运行与旧构建运行加载的不是本仓库的 DLL，
/// 框架代码仍须能被 JIT。
/// </para>
/// </remarks>
internal sealed class MugirlTarget
{
    /// <summary>正式运行的 packageId；参数文件没有给出 targetPackageId 时使用。</summary>
    public const string DefaultPackageId = "HAR.MugirlRace";

    public const string AssemblyName = "MugirlRace";

    /// <summary>被测 Mod 的 Harmony ID。属于公开 API（依赖 Mod 按它排序补丁），旧构建也使用它。</summary>
    public const string HarmonyId = "MugirlMod.Mod";

    private MugirlTarget(ModContentPack mod, Assembly? assembly)
    {
        Mod = mod;
        Assembly = assembly;
    }

    public ModContentPack Mod { get; }

    /// <summary>该 Mod 的程序集加载器载入的 MugirlRace.dll；被 RimWorld 拒绝时为 null。</summary>
    public Assembly? Assembly { get; }

    public string PackageId => Mod.PackageId;

    /// <summary>是否为开发目录的正式 Mod（而不是临时旧构建）。</summary>
    public bool IsDefaultPackage => string.Equals(Mod.PackageId, DefaultPackageId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 找到被测构建。规则：加载了 MugirlRace 程序集的运行中 Mod 恰好一个，且其 packageId 等于 <paramref name="packageId"/>；
    /// 没有 Mod 加载该程序集时，退回 packageId 相同的 Mod（程序集被拒绝的情况，由 startup 套件报告）。
    /// 多个 Mod 加载同名程序集时只认 packageId 匹配的那个，并在 <paramref name="problem"/> 中写明冲突。
    /// </summary>
    public static MugirlTarget? Detect(string packageId, out string problem)
    {
        var loaders = new List<(ModContentPack Mod, Assembly Assembly)>();
        ModContentPack? byPackageId = null;
        foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading)
        {
            if (string.Equals(mod.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            {
                byPackageId = mod;
            }

            if (mod.assemblies.loadedAssemblies.FirstOrDefault(a => string.Equals(a.GetName().Name, AssemblyName, StringComparison.Ordinal)) is { } assembly)
            {
                loaders.Add((mod, assembly));
            }
        }

        problem = loaders.Count > 1
            ? $"{loaders.Count} running mods load an assembly named {AssemblyName}: {string.Join(", ", loaders.Select(l => l.Mod.PackageIdPlayerFacing))}"
            : string.Empty;
        if (byPackageId is null)
        {
            if (loaders.Count > 0)
            {
                problem = (problem.Length > 0 ? problem + "; " : string.Empty)
                    + $"{loaders[0].Mod.PackageIdPlayerFacing} loads {AssemblyName}, but the expected target is {packageId}";
            }

            return null;
        }

        Assembly? own = loaders.FirstOrDefault(l => l.Mod == byPackageId).Assembly;
        return new MugirlTarget(byPackageId, own);
    }

    /// <summary>该 Def 是否由被测 Mod 定义（含由它的 Def 派生的隐含 Def）。</summary>
    public bool Owns(Def? def) => def?.modContentPack == Mod;

    /// <summary>被测 Mod 定义的 <typeparamref name="T"/>，按 defName 排序（顺序决定性，报告可比较）。</summary>
    public List<T> Defs<T>()
        where T : Def
        => [.. DefDatabase<T>.AllDefsListForReading.Where(d => d.modContentPack == Mod).OrderBy(d => d.defName, StringComparer.Ordinal)];

    /// <summary>
    /// 被测 Mod 的全部 Def（任意 Def 类型，含由它的 Def 派生、modContentPack 指向它的隐含 Def），按类型名与 defName 排序。
    /// 读存档时用来认出 Mod 拥有的节点（<see cref="SaveFacts"/>、<see cref="SaveDiff"/>）。
    /// </summary>
    public List<Def> AllDefs()
    {
        var defs = new List<Def>();
        foreach (Type defType in GenDefDatabase.AllDefTypesWithDatabases().OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            defs.AddRange(GenDefDatabase.GetAllDefsInDatabaseForDef(defType)
                .Where(d => d.modContentPack == Mod)
                .OrderBy(d => d.defName, StringComparer.Ordinal));
        }

        return defs;
    }

    /// <summary>被测程序集中按全名查找的类型；程序集没有加载或类型不存在时为 null。</summary>
    public Type? FindType(string fullName) => Assembly?.GetType(fullName, throwOnError: false);

    public override string ToString()
        => $"{Mod.Name} [{Mod.PackageIdPlayerFacing}] at {Mod.RootDir}; assembly {Assembly?.Location ?? "(not loaded)"}";
}
