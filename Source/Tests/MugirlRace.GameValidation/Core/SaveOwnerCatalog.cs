using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>
/// 求值 owner 选择器与认出组件标签所需的 Def→组件关系：旧 defName 按旧版语料（<see cref="LegacyCorpus"/>），
/// 新 defName 按当前构建（游戏内由 DefDatabase 提供），组件类一律经升级清单的类型映射换成新名后比较。
/// </summary>
/// <remarks>只用数据，不访问 Verse 状态；当前构建的关系由调用方传入，离线测试可以直接构造。</remarks>
internal sealed class SaveOwnerCatalog : ISaveOwnerContext
{
    private readonly SaveNameMap map;
    private readonly Dictionary<(string Root, string Def), HashSet<string>> comps = [];
    private readonly Dictionary<string, HashSet<string>> compLabels = new(StringComparer.Ordinal);

    /// <param name="map">升级清单（类型、Def 与标签的映射）。</param>
    /// <param name="corpus">旧版语料（旧 Def 的组件与各类型的标签）。</param>
    /// <param name="current">当前构建中带组件的 Def：(根类型, defName, 组件类全名)。</param>
    public SaveOwnerCatalog(SaveNameMap map, LegacyCorpus corpus, IEnumerable<(string Root, string Def, string CompClass)> current,
        IReadOnlyDictionary<string, HashSet<string>>? currentLabels = null)
    {
        this.map = map;
        foreach ((string root, string def) in corpus.Defs)
        {
            string? renamed = map.Def(root, def);
            foreach (string legacyComp in corpus.CompsOf(root, def))
            {
                string comp = map.Type(legacyComp) ?? legacyComp;
                AddComp(root, def, comp);
                if (renamed is not null)
                {
                    AddComp(root, renamed, comp);
                }

                if (root != "ThingDef" || !SaveXml.IsModClass(legacyComp))
                {
                    continue;
                }

                // 组件标签：旧构建写出的标签，加上清单中该组件（新名）的标签行改出的新标签。
                IEnumerable<string> labels = corpus.LabelsOf(legacyComp)
                    .Concat(map.LabelRows.Where(row => row.Owner.ThingCompClass == comp && row.New is not null).Select(row => row.New!));
                foreach (string label in labels)
                {
                    AddLabel(def, label);
                    if (renamed is not null)
                    {
                        AddLabel(renamed, label);
                    }
                }
            }
        }

        foreach ((string root, string def, string comp) in current)
        {
            AddComp(root, def, comp);
        }

        if (currentLabels is not null)
        {
            foreach (((string root, string def), HashSet<string> classes) in comps)
            {
                if (root != "ThingDef")
                {
                    continue;
                }

                foreach (string comp in classes)
                {
                    if (currentLabels.TryGetValue(comp, out HashSet<string>? roots))
                    {
                        foreach (string label in roots)
                        {
                            AddLabel(def, label);
                        }
                    }
                }
            }
        }
    }

    /// <summary>登记了组件标签的 ThingDef 数。</summary>
    public int ThingDefsWithModCompLabels => compLabels.Count;

    /// <summary>稳定、独立的比较范围快照，供离线复核实际游戏的 Def→根标签关系。</summary>
    public SortedDictionary<string, string[]> SnapshotCompLabels()
        => new(compLabels.ToDictionary(p => p.Key, p => p.Value.Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal), StringComparer.Ordinal);

    public string? MappedClassOf(XElement element)
        => SaveXml.ClassOf(element) is { } name ? map.Type(name) ?? name : null;

    public bool DefHasComp(string root, string defName, string compClass)
        => comps.TryGetValue((root, defName), out HashSet<string>? set) && set.Contains(compClass);

    /// <summary><paramref name="label"/> 是否是雪牛娘组件写在 <paramref name="thingDef"/> 的物品节点里的标签（旧名或新名）。</summary>
    public bool IsModCompLabel(string thingDef, string label)
        => compLabels.TryGetValue(thingDef, out HashSet<string>? set) && set.Contains(label);

    private void AddComp(string root, string def, string comp)
    {
        if (!comps.TryGetValue((root, def), out HashSet<string>? set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            comps[(root, def)] = set;
        }

        set.Add(comp);
    }

    private void AddLabel(string def, string label)
    {
        if (!compLabels.TryGetValue(def, out HashSet<string>? set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            compLabels[def] = set;
        }

        set.Add(label);
    }
}
