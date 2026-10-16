using System.Linq;
using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>DOM 步骤的运行环境：存档、表格、Def 目录、存档索引、会话数据，以及框架步骤之间传递的效果。</summary>
internal sealed class UpgradeContext
{
    private readonly Dictionary<(string Root, string Def), HashSet<string>?> catalogComps = [];
    private SaveIndex? index;

    /// <param name="tables">按 <paramref name="sourceSchema"/> 过滤后的表格视图（<see cref="UpgradeTables.ForSource"/>）。</param>
    internal UpgradeContext(SaveDocument document, UpgradeTables tables, IDefCatalog catalog, SessionData data, int sourceSchema)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Tables = tables ?? throw new ArgumentNullException(nameof(tables));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Data = data ?? throw new ArgumentNullException(nameof(data));
        SourceSchema = sourceSchema;
    }

    internal SaveDocument Document { get; }

    internal UpgradeTables Tables { get; }

    internal IDefCatalog Catalog { get; }

    /// <summary>传给读档后步骤的标志与数据。</summary>
    internal SessionData Data { get; }

    /// <summary>存档的来源结构版本（版本戳，没有时为 0）。</summary>
    internal int SourceSchema { get; }

    internal SaveIndex Index => index ?? throw new InvalidOperationException("The save index has not been built yet.");

    internal ContainerRules Containers => Tables.Containers;

    /// <summary>
    /// 已删除对象的加载 ID（ThingID、<c>Lord_&lt;id&gt;</c>、<c>Bill_&lt;recipe&gt;_&lt;id&gt;</c>，含随所在子树删除的对象）；
    /// 最后的加载 ID 步骤把对它们的引用置空或删除。
    /// </summary>
    internal HashSet<string> RemovedLoadIds { get; } = new(StringComparer.Ordinal);

    /// <summary>已删除的配方；加载 ID 步骤把 <c>Bill_&lt;recipe&gt;_&lt;n&gt;</c> 引用置空或删除。</summary>
    internal HashSet<string> RemovedRecipes { get; } = new(StringComparer.Ordinal);

    /// <summary>类型步骤改写过 <c>Class</c> 的元素及其原值。</summary>
    internal Dictionary<XmlElement, string> LegacyClasses { get; } = [];

    /// <summary>存档带有 MooGirl 时代标记。</summary>
    internal bool MooGirlDetected { get; set; }

    /// <summary>遍历存档建立索引；由流水线在第一个步骤之前调用一次。</summary>
    /// <param name="extraNames">表格之外还需要按名收集的元素名（框架步骤使用的原版结构）。</param>
    internal void BuildIndex(IEnumerable<string> extraNames)
    {
        index = SaveIndex.Build(
            Document.Game,
            Tables.IndexNames.Concat(extraNames),
            Tables.MooGirlTypePrefixes,
            Tables.MooGirlDefPatterns,
            Tables.MapClass,
            def => Tables.RenamedDef("ThingDef", def));
    }

    /// <summary>元素当前 <c>Class</c> 按类型表映射后的存档类名；没有 <c>Class</c> 时为 null。</summary>
    internal string? MappedClassOf(XmlElement element)
    {
        string value = element.GetAttribute("Class");
        return value.Length == 0 ? null : Tables.MapClass(value);
    }

    /// <summary>元素在升级前的 <c>Class</c>（类型步骤改写前的值）；没有时为 null。</summary>
    internal string? OriginalClassOf(XmlElement element)
    {
        if (LegacyClasses.TryGetValue(element, out string? original))
        {
            return original;
        }

        string value = element.GetAttribute("Class");
        return value.Length == 0 ? null : value;
    }

    /// <summary>元素是否仍在 <c>game</c> 子树中（没有被本次升级删除或替换）。</summary>
    internal bool IsLive(XmlElement element)
    {
        if (index is { RemovedCount: 0 } && element.ParentNode is not null)
        {
            return true;
        }

        for (XmlNode? node = element; node is not null; node = node.ParentNode)
        {
            if (node == Document.Game)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 存档中 def 为 <paramref name="defInSave"/> 的元素是否带有组件 <paramref name="compSaveName"/>：先查旧版 def→组件关系，
    /// 没有登记时按改名后的 defName 查当前 Def 目录（第三方种族等）。
    /// </summary>
    internal bool DefHasComp(string root, string defInSave, string compSaveName)
    {
        if (Tables.LegacyCompsOf(root, defInSave) is { } legacy)
        {
            return legacy.Contains(compSaveName, SaveTypeNames.Comparer);
        }

        DefRow? row = Tables.FindDef(root, defInSave);
        if (row is { New: null })
        {
            return false;
        }

        string current = row?.New ?? defInSave;
        if (!catalogComps.TryGetValue((root, current), out HashSet<string>? comps))
        {
            comps = Catalog.CompClasses(root, current) is { } names
                ? new HashSet<string>(names.Select(SaveTypeNames.Normalize), SaveTypeNames.Comparer)
                : null;
            catalogComps.Add((root, current), comps);
        }

        return comps is not null && comps.Contains(compSaveName);
    }

    /// <summary>步骤应用成功后，把计划附带的效果并入环境。</summary>
    internal void Commit(EditPlan plan)
    {
        RemovedLoadIds.UnionWith(plan.RemovedLoadIds);
        RemovedRecipes.UnionWith(plan.RemovedRecipes);
        foreach ((XmlElement element, string original) in plan.LegacyClasses)
        {
            LegacyClasses.TryAdd(element, original);
        }

        foreach (string flag in plan.SessionFlags)
        {
            Data.SetFlag(flag);
        }

        Data.ResetLordIds.UnionWith(plan.ResetLordIds);
    }
}
