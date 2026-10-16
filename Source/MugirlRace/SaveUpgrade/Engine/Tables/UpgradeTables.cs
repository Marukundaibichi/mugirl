using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 编译后的升级表格：旧名称的唯一来源。DOM 流水线、<c>BackCompatibleDefName</c>/<c>WasDefRemoved</c> 后缀、设置文件升级与升级清单
/// 都读同一份表格。
/// </summary>
/// <remarks>
/// <para>
/// 构建只依赖登记的数据，不读取 Verse 状态，可在离线验证与读档工作线程中使用；构建后不可变，可被多个线程并发读取。
/// </para>
/// <para>
/// 读档时流水线只使用 <see cref="ForSource"/> 给出的视图：其中只有结构版本高于存档来源版本的行与步骤。
/// 结构声明（值上下文、枚举位置、可移除容器、并行组、旧版组件关系、MooGirl 时代模式）描述存档形状，不随版本过滤。
/// </para>
/// </remarks>
internal sealed class UpgradeTables
{
    // StaticCacheLifecycle: 进程级不可变表格，首次使用时从本程序集的模块构建；构建失败的结果同样缓存（不重复抛出）。
    private static readonly Lazy<(UpgradeTables? Tables, Exception? Error)> DefaultTables = new(BuildDefault, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Dictionary<string, TypeRow> typesByOld;
    private readonly Dictionary<(string Root, string Old), DefRow> defsByKey;
    private readonly Dictionary<string, Dictionary<string, string>> defRenamesByOld;
    private readonly Dictionary<string, List<LabelRow>> labelsByOld;
    private readonly Dictionary<(string Enum, string Old), string> enumMembers;
    private readonly Dictionary<string, ValueContext> contextsByName;
    private readonly Dictionary<string, Dictionary<string, ValueRow>> valuesByContext;
    private readonly Dictionary<string, LordJobPolicyRow> lordPolicies;
    private readonly Dictionary<(string Root, string Def), string[]> legacyComps;

    internal UpgradeTables(UpgradeTableBuilder builder, IReadOnlyList<(string Name, int Order)> modules, IReadOnlyList<DefRow> derivedDefs)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Modules = modules;
        TypeRows = [.. builder.Types];
        DefRows = [.. builder.Defs];
        DerivedDefRows = derivedDefs;
        LabelRows = [.. builder.Labels];
        EnumRows = [.. builder.Enums];
        EnumSites = [.. builder.EnumSites];
        ValueContexts = [.. builder.ValueContexts];
        ValueRows = [.. builder.Values];
        DefaultRows = [.. builder.Defaults];
        JobPolicies = [.. builder.JobPolicies];
        LordPolicies = [.. builder.LordPolicies];
        SettingsRows = [.. builder.Settings];
        LegacyCompsRows = [.. builder.LegacyComps];
        RemovableContainers = [.. ContainerRules.VanillaContainers, .. builder.Containers];
        ParallelGroups = [.. builder.ParallelGroups];
        DomSteps = [.. builder.Steps.OfType<DomUpgradeStep>()];
        PostLoadSteps = [.. builder.Steps.OfType<PostLoadUpgradeStep>()];
        MooGirlTypePrefixes = [.. builder.MooGirlTypePrefixes.Distinct(StringComparer.Ordinal)];
        MooGirlDefPatterns = [.. builder.MooGirlDefPatterns.Distinct(StringComparer.Ordinal).Select(p => new GlobPattern(p))];
        SourceSchema = null;
        Containers = new ContainerRules(RemovableContainers, ParallelGroups);
        (typesByOld, defsByKey, defRenamesByOld, labelsByOld, enumMembers, contextsByName, valuesByContext, lordPolicies, legacyComps) = BuildLookups();
        IndexNames = CollectIndexNames();
    }

    // 版本视图：行与步骤按结构版本过滤，结构声明原样共享。
    private UpgradeTables(UpgradeTables all, int sourceSchema)
    {
        Modules = all.Modules;
        TypeRows = [.. all.TypeRows.Where(row => row.Schema > sourceSchema)];
        DefRows = [.. all.DefRows.Where(row => row.Schema > sourceSchema)];
        DerivedDefRows = [.. all.DerivedDefRows.Where(row => row.Schema > sourceSchema)];
        LabelRows = [.. all.LabelRows.Where(row => row.Schema > sourceSchema)];
        EnumRows = [.. all.EnumRows.Where(row => row.Schema > sourceSchema)];
        EnumSites = all.EnumSites;
        ValueContexts = all.ValueContexts;
        ValueRows = [.. all.ValueRows.Where(row => row.Schema > sourceSchema)];
        DefaultRows = [.. all.DefaultRows.Where(row => row.Schema > sourceSchema)];
        JobPolicies = [.. all.JobPolicies.Where(row => row.Schema > sourceSchema)];
        LordPolicies = [.. all.LordPolicies.Where(row => row.Schema > sourceSchema)];
        SettingsRows = all.SettingsRows;
        LegacyCompsRows = [.. all.LegacyCompsRows.Where(row => row.Schema > sourceSchema)];
        RemovableContainers = all.RemovableContainers;
        ParallelGroups = all.ParallelGroups;
        DomSteps = [.. all.DomSteps.Where(step => step.Schema > sourceSchema)];
        PostLoadSteps = [.. all.PostLoadSteps.Where(step => step.Schema > sourceSchema)];
        MooGirlTypePrefixes = all.MooGirlTypePrefixes;
        MooGirlDefPatterns = all.MooGirlDefPatterns;
        SourceSchema = sourceSchema;
        Containers = all.Containers;
        (typesByOld, defsByKey, defRenamesByOld, labelsByOld, enumMembers, contextsByName, valuesByContext, lordPolicies, legacyComps) = BuildLookups();
        IndexNames = CollectIndexNames();
    }

    /// <summary>已登记的模块（名称, 顺序），按顺序排列。</summary>
    internal IReadOnlyList<(string Name, int Order)> Modules { get; }

    /// <summary>本视图过滤所用的来源结构版本；完整表格为 null。</summary>
    internal int? SourceSchema { get; }

    internal IReadOnlyList<TypeRow> TypeRows { get; }

    /// <summary>模块登记的 Def 行（写入升级清单）。</summary>
    internal IReadOnlyList<DefRow> DefRows { get; }

    /// <summary>由原版隐含 Def 规则派生的行（不写入升级清单，由登记行唯一确定）。</summary>
    internal IReadOnlyList<DefRow> DerivedDefRows { get; }

    internal IReadOnlyList<LabelRow> LabelRows { get; }

    internal IReadOnlyList<EnumRow> EnumRows { get; }

    internal IReadOnlyList<EnumSite> EnumSites { get; }

    internal IReadOnlyList<ValueContext> ValueContexts { get; }

    internal IReadOnlyList<ValueRow> ValueRows { get; }

    internal IReadOnlyList<DefaultRow> DefaultRows { get; }

    internal IReadOnlyList<JobDriverPolicyRow> JobPolicies { get; }

    internal IReadOnlyList<LordJobPolicyRow> LordPolicies { get; }

    /// <summary>设置文件升级行（按设置文件自己的 <c>settingsSchema</c> 应用，目标版本是 <see cref="SaveSchema.SettingsCurrent"/>）。</summary>
    internal IReadOnlyList<SettingsRow> SettingsRows { get; }

    internal IReadOnlyList<LegacyCompsRow> LegacyCompsRows { get; }

    /// <summary>原版可移除容器加模块登记的容器。</summary>
    internal IReadOnlyList<RemovableContainer> RemovableContainers { get; }

    internal IReadOnlyList<ParallelGroup> ParallelGroups { get; }

    /// <summary>模块 DOM 步骤，按 (模块顺序, 登记顺序) 排列；在框架的表格步骤之后、加载 ID 步骤之前运行。</summary>
    internal IReadOnlyList<DomUpgradeStep> DomSteps { get; }

    /// <summary>模块读档后步骤，按 (模块顺序, 登记顺序) 排列。</summary>
    internal IReadOnlyList<PostLoadUpgradeStep> PostLoadSteps { get; }

    internal IReadOnlyList<string> MooGirlTypePrefixes { get; }

    internal IReadOnlyList<GlobPattern> MooGirlDefPatterns { get; }

    internal ContainerRules Containers { get; }

    /// <summary>存档索引需要按名收集的元素名。</summary>
    internal IReadOnlySet<string> IndexNames { get; }

    /// <summary>
    /// 本程序集全部模块编译出的表格。构建失败时返回 false 与异常（结果缓存，不重复构建）。
    /// </summary>
    internal static bool TryGetDefault([NotNullWhen(true)] out UpgradeTables? tables, [NotNullWhen(false)] out Exception? error)
    {
        (tables, error) = DefaultTables.Value;
        if (tables is not null)
        {
            error = null;
            return true;
        }

        error ??= new InvalidOperationException("Save upgrade tables are unavailable.");
        return false;
    }

    /// <summary>本程序集全部模块编译出的表格；构建失败时抛出构建异常。</summary>
    internal static UpgradeTables Default => TryGetDefault(out UpgradeTables? tables, out Exception? error)
        ? tables
        : throw new UpgradeTableException("Save upgrade tables could not be built.", error);

    /// <summary>
    /// 来源结构版本为 <paramref name="sourceSchema"/> 的存档适用的视图：只含结构版本高于它的行与步骤。
    /// 所有行都适用时（没有版本戳的旧档）返回本对象。视图只能继续按更高的来源版本过滤。
    /// </summary>
    internal UpgradeTables ForSource(int sourceSchema)
    {
        if (SourceSchema is { } filtered && sourceSchema < filtered)
        {
            throw new InvalidOperationException("Tables filtered for source schema " + filtered + " cannot serve source schema " + sourceSchema + ".");
        }

        bool allApply = TypeRows.All(row => row.Schema > sourceSchema) && DefRows.All(row => row.Schema > sourceSchema)
            && LabelRows.All(row => row.Schema > sourceSchema) && EnumRows.All(row => row.Schema > sourceSchema)
            && ValueRows.All(row => row.Schema > sourceSchema) && DefaultRows.All(row => row.Schema > sourceSchema)
            && JobPolicies.All(row => row.Schema > sourceSchema) && LordPolicies.All(row => row.Schema > sourceSchema)
            && LegacyCompsRows.All(row => row.Schema > sourceSchema)
            && DomSteps.All(step => step.Schema > sourceSchema) && PostLoadSteps.All(step => step.Schema > sourceSchema);
        return allApply ? this : new UpgradeTables(this, sourceSchema);
    }

    /// <summary>旧类型名对应的行；查找不区分大小写。</summary>
    internal TypeRow? FindType(string oldName) =>
        typesByOld.TryGetValue(SaveTypeNames.Normalize(oldName), out TypeRow? row) ? row : null;

    /// <summary>
    /// 存档 <c>Class</c> 值（含泛型参数）按类型表映射后的存档类名；没有改名的部分保持原样（规范化）。删除的类型不改名。
    /// </summary>
    internal string MapClass(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return SaveTypeNames.Rewrite(value, name => FindType(name)?.NewSaveName) ?? SaveTypeNames.Normalize(value.Trim());
    }

    /// <summary>(根, 旧名) 对应的行，含隐含 Def 派生行。</summary>
    internal DefRow? FindDef(string root, string oldName) =>
        defsByKey.TryGetValue((root, oldName), out DefRow? row) ? row : null;

    /// <summary>(根, 名称) 改名后的名称；未登记或已删除时为 null。</summary>
    internal string? RenamedDef(string root, string name) => FindDef(root, name)?.New;

    /// <summary>(根, 名称) 是否登记为已删除的 Def（含隐含 Def）。</summary>
    internal bool IsDroppedDef(string root, string name) => FindDef(root, name) is { New: null };

    /// <summary>以 <paramref name="oldName"/> 为旧名的全部改名（根 → 新名）；供 <c>BackCompatibleDefName</c> 后缀快速判断。</summary>
    internal bool TryGetDefRenames(string oldName, [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? renamesByRoot)
    {
        if (defRenamesByOld.TryGetValue(oldName, out Dictionary<string, string>? byRoot))
        {
            renamesByRoot = byRoot;
            return true;
        }

        renamesByRoot = null;
        return false;
    }

    internal IReadOnlyList<LabelRow> LabelRowsFor(string oldLabel) =>
        labelsByOld.TryGetValue(oldLabel, out List<LabelRow>? rows) ? rows : [];

    internal string? RenamedEnumMember(string enumName, string member) =>
        enumMembers.TryGetValue((enumName, member), out string? renamed) ? renamed : null;

    internal ValueContext? FindContext(string name) => contextsByName.TryGetValue(name, out ValueContext? context) ? context : null;

    internal IReadOnlyDictionary<string, ValueRow> ValuesFor(string context) =>
        valuesByContext.TryGetValue(context, out Dictionary<string, ValueRow>? rows) ? rows : new Dictionary<string, ValueRow>();

    internal LordJobPolicyRow? LordPolicyFor(string classValue) =>
        lordPolicies.TryGetValue(SaveTypeNames.Normalize(classValue), out LordJobPolicyRow? row) ? row : null;

    /// <summary>旧版 def 的组件类（已按类型表映射为存档类名）；没有登记时为 null。</summary>
    internal IReadOnlyList<string>? LegacyCompsOf(string root, string defName) =>
        legacyComps.TryGetValue((root, defName), out string[]? comps) ? comps : null;

    private static (UpgradeTables?, Exception?) BuildDefault()
    {
        try
        {
            return (UpgradeTableBuilder.Build(UpgradeModuleDiscovery.Discover(typeof(UpgradeTables).Assembly)), null);
        }
        catch (Exception exception)
        {
            // 模块登记代码可能抛出任意异常；结果缓存为“不可用”，由调用方报告一次，读档时整次升级标记为失败。
            return (null, exception);
        }
    }

    private (
        Dictionary<string, TypeRow> Types,
        Dictionary<(string Root, string Old), DefRow> Defs,
        Dictionary<string, Dictionary<string, string>> DefRenames,
        Dictionary<string, List<LabelRow>> Labels,
        Dictionary<(string Enum, string Old), string> EnumMembers,
        Dictionary<string, ValueContext> Contexts,
        Dictionary<string, Dictionary<string, ValueRow>> Values,
        Dictionary<string, LordJobPolicyRow> LordPolicies,
        Dictionary<(string Root, string Def), string[]> LegacyComps) BuildLookups()
    {
        Dictionary<string, TypeRow> types = TypeRows.ToDictionary(row => SaveTypeNames.Normalize(row.Old), SaveTypeNames.Comparer);
        var defRenames = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (DefRow row in DefRows.Concat(DerivedDefRows))
        {
            if (row.New is null)
            {
                continue;
            }

            if (!defRenames.TryGetValue(row.Old, out Dictionary<string, string>? byRoot))
            {
                byRoot = new Dictionary<string, string>(StringComparer.Ordinal);
                defRenames.Add(row.Old, byRoot);
            }

            byRoot[row.Root] = row.New;
        }

        string Map(string comp) =>
            SaveTypeNames.Rewrite(comp, name => types.TryGetValue(SaveTypeNames.Normalize(name), out TypeRow? row) ? row.NewSaveName : null)
            ?? SaveTypeNames.Normalize(comp.Trim());

        return (
            types,
            DefRows.Concat(DerivedDefRows).ToDictionary(row => (row.Root, row.Old)),
            defRenames,
            LabelRows.GroupBy(row => row.Old, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal),
            EnumRows.ToDictionary(row => (row.Enum, row.Old), row => row.New),
            ValueContexts.ToDictionary(context => context.Name, StringComparer.Ordinal),
            ValueRows.GroupBy(row => row.Context, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToDictionary(row => row.Old, StringComparer.Ordinal), StringComparer.Ordinal),
            LordPolicies.ToDictionary(row => SaveTypeNames.Normalize(row.Type), SaveTypeNames.Comparer),
            LegacyCompsRows.ToDictionary(row => (row.Root, row.DefName), row => row.CompClasses.Select(Map).ToArray()));
    }

    private HashSet<string> CollectIndexNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        names.UnionWith(LabelRows.Select(row => row.Old));
        names.UnionWith(EnumSites.Select(site => site.Label));
        names.UnionWith(ValueContexts.Select(context => context.Label));
        foreach (DefaultRow row in DefaultRows)
        {
            names.UnionWith(row.Owner.IndexNames);
        }

        foreach (DomUpgradeStep step in DomSteps)
        {
            names.UnionWith(step.IndexNames);
            names.UnionWith(step.ConsumedLabels);
        }

        return names;
    }
}
