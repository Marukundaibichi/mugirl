using Mugirl.GameValidation.Suites;

namespace Mugirl.GameValidation.Core;

/// <summary>全部已登记的套件与按名选择。</summary>
/// <remarks>
/// <para>
/// 只用原版 API 与 defName 的套件放在 Suites/，直接登记在 <see cref="All"/> 中。引用被测 Mod 类型的套件放在 Suites/Mod/，
/// 通过 <see cref="AddModSuites"/> 的实现（Suites/Mod/ModSuiteRegistration.cs）登记；tools/Test-InGame.ps1 -Plumbing 用
/// <c>-p:MugirlPlumbingOnly=true</c> 构建，不引用 MugirlRace、不编译 Suites/Mod/，Mod 源码编译失败时框架自检照样能跑，
/// 这些套件则显示为“未编译”而跳过。
/// </para>
/// <para>
/// 新功能套件：替换下面对应的 <see cref="ValidationSuite.Planned"/> 登记即可。需要 HAR.MugirlRace 的套件声明
/// <see cref="SuiteNeeds.MugirlMod"/>，需要截图或界面的再加 <see cref="SuiteNeeds.RenderedFrames"/>。
/// 测试程序集中的类型不能继承被测 Mod 的类型，也不能有值类型（枚举、结构体）的被测 Mod 字段（迭代器与 lambda 的局部变量也会变成字段）：
/// -Plumbing 运行不加载 MugirlRace.dll，这些会让本程序集的 GetTypes() 失败，RimWorld 随之拒绝整个测试程序集。
/// </para>
/// </remarks>
internal static partial class SuiteCatalog
{
    public const string DefaultSelection = "default";
    public const string AllSelection = "all";

    /// <summary>Suites/Mod/ 中套件的名字；未编译时以同名的跳过项占位。</summary>
    public const string StartupSuiteName = "startup";

    public const string UiSuiteName = "ui";

    public const string UpgradeSuiteName = "upgrade";

    /// <summary>运行顺序即列表顺序。</summary>
    // StaticCacheLifecycle: 进程级不可变的套件登记表，首次访问时创建。
    public static IReadOnlyList<ValidationSuite> All { get; } = CreateAll();

    private static List<ValidationSuite> CreateAll()
    {
        var modSuites = new Dictionary<string, Func<ValidationSuite>>(StringComparer.Ordinal);
        AddModSuites(modSuites);
        return
        [
            PlumbingSuite.Create(),
            FixtureGateSuite.Create(),
            ReferenceResaveSuite.Create(),
            MilkMigrationSuite.Create(),
            ThinkTreeMigrationSuite.Create(),
            ThinkTreeWarningSuite.Create(),
            ModSuite(modSuites, StartupSuiteName),
            SpawnSuite.Create(),
            SaveLoadSuite.Create(),
            RenderSuite.Create(),
            ModSuite(modSuites, UiSuiteName),
            ContentSweepSuite.Create(),
            ModSuite(modSuites, UpgradeSuiteName, inDefaultRun: false),
            ValidationSuite.Planned("lance", "lance charge flight, wall breaking and landing; motion blur screenshot pair with and without the native ShaderDef, magenta fallback detection"),
            ValidationSuite.Planned("outline", "extra outline on and off: screenshot pair, outline width setting, no ShaderRegistry fallback"),
            ValidationSuite.Planned("milk", "milk gauge gizmo and fast milking setting, real milking job on a lactating Mugirl, merged multi-select gizmo"),
            ValidationSuite.Planned("weapon-wheel", "weapon wheel cycling, hunting weapon switch regressions, recursion guard, save/load of wheel slots"),
            ValidationSuite.Planned("throw", "throw weight and connection rules, flight and landing interpolation at TickRateMultiplier 20000, dunk"),
        ];
    }

    /// <summary>登记引用被测 Mod 类型的套件；实现只在引用了 MugirlRace 的构建中编译。</summary>
    static partial void AddModSuites(Dictionary<string, Func<ValidationSuite>> factories);

    private static ValidationSuite ModSuite(Dictionary<string, Func<ValidationSuite>> factories, string name, bool inDefaultRun = true)
        => factories.TryGetValue(name, out Func<ValidationSuite>? create)
            ? create()
            : ValidationSuite.Unavailable(name, "not compiled into this build (plumbing-only build without a MugirlRace reference)", inDefaultRun);

    /// <summary>把请求（套件名、"default"、"all"）解析为按 <see cref="All"/> 顺序排列、去重的计划。</summary>
    public static bool TryResolve(IReadOnlyList<string> requested, out List<ValidationSuite> plan, out string problem)
    {
        var selected = new HashSet<ValidationSuite>();
        var unknown = new List<string>();
        foreach (string name in requested)
        {
            if (string.Equals(name, AllSelection, StringComparison.OrdinalIgnoreCase))
            {
                selected.UnionWith(All.Where(s => s.InAllRun));
            }
            else if (string.Equals(name, DefaultSelection, StringComparison.OrdinalIgnoreCase))
            {
                selected.UnionWith(All.Where(s => s.InDefaultRun));
            }
            else if (All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) is { } suite)
            {
                selected.Add(suite);
            }
            else
            {
                unknown.Add(name);
            }
        }

        plan = [.. All.Where(selected.Contains)];
        if (unknown.Count > 0)
        {
            problem = $"unknown suite(s) {string.Join(", ", unknown)}; known: {string.Join(", ", All.Select(s => s.Name))}, {DefaultSelection}, {AllSelection}";
            return false;
        }

        problem = string.Empty;
        return true;
    }
}
