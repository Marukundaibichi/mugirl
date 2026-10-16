using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// 对 <c>game</c> 子树做唯一一次完整遍历，收集所有步骤需要的元素：带 <c>Class</c> 的元素、物品、健康状态、账单、Lord、
/// 登记过名称的元素和 MooGirl 时代标记。步骤只查询索引，不再各自遍历存档。
/// </summary>
/// <remarks>
/// 索引保存元素引用。编辑原语改名元素（新建元素替换旧元素）时通知索引，查询结果按替换关系解析到新元素；
/// 删除不更新列表，调用方用 <see cref="UpgradeContext.IsLive"/> 过滤已脱离文档的元素。
/// </remarks>
internal sealed class SaveIndex
{
    private readonly HashSet<string> indexedNames;
    private readonly string[] mooGirlTypePrefixes;
    private readonly GlobPattern[] mooGirlDefPatterns;
    private readonly HashSet<string> mooGirlExactDefs = new(StringComparer.Ordinal);
    private readonly Func<string, string> classMapper;
    private readonly Func<string, string?> renamedThingDef;
    private readonly Dictionary<string, List<XmlElement>> byClass = new(SaveTypeNames.Comparer);
    private readonly Dictionary<string, List<XmlElement>> byName = new(StringComparer.Ordinal);
    private readonly List<IndexedThing> things = [];
    private readonly Dictionary<XmlElement, IndexedThing> thingByElement = [];
    private readonly Dictionary<string, List<IndexedThing>> thingsByDef = new(StringComparer.Ordinal);
    private readonly List<IndexedHediff> hediffs = [];
    private readonly Dictionary<XmlElement, string> hediffDefByElement = [];
    private readonly List<XmlElement> bills = [];
    private readonly HashSet<XmlElement> billSet = [];
    private readonly List<XmlElement> lords = [];
    private readonly HashSet<XmlElement> lordSet = [];
    private readonly List<XmlElement> mooGirlClassElements = [];
    private readonly List<XmlElement> mooGirlLeaves = [];
    private readonly Dictionary<XmlElement, XmlElement> replacements = [];
    private Dictionary<string, List<XmlElement>>? byMappedClass;

    private SaveIndex(IEnumerable<string> names, IEnumerable<string> typePrefixes, IEnumerable<GlobPattern> defPatterns, Func<string, string> mapper, Func<string, string?> thingDefRenames)
    {
        indexedNames = new HashSet<string>(names, StringComparer.Ordinal);
        mooGirlTypePrefixes = [.. typePrefixes];
        List<GlobPattern> wildcardPatterns = [];
        foreach (GlobPattern pattern in defPatterns)
        {
            if (pattern.Text.Contains('*', StringComparison.Ordinal))
            {
                wildcardPatterns.Add(pattern);
            }
            else
            {
                mooGirlExactDefs.Add(pattern.Text);
            }
        }

        // 精确历史名称用哈希查找；每个存档叶节点只需检查极少量真正的通配模式。
        mooGirlDefPatterns = [.. wildcardPatterns];
        classMapper = mapper;
        renamedThingDef = thingDefRenames;
    }

    /// <summary>遍历过的元素数。</summary>
    internal int ElementCount { get; private set; }

    /// <summary>流水线删除过的元素数；为 0 时所有已索引元素都还在文档中。</summary>
    internal int RemovedCount { get; private set; }

    /// <summary>按结构识别的物品（元素已沿改名替换关系解析）。</summary>
    internal IReadOnlyList<IndexedThing> Things => Resolved(things);

    /// <summary><c>hediffSet/hediffs/li</c> 中的健康状态（元素已解析）。</summary>
    internal IReadOnlyList<IndexedHediff> Hediffs =>
        replacements.Count == 0 ? hediffs : [.. hediffs.Select(hediff => hediff with { Element = Resolve(hediff.Element) })];

    /// <summary><c>bills/li</c> 元素（账单）。</summary>
    internal IEnumerable<XmlElement> Bills => Resolved(bills);

    /// <summary><c>lords/li</c> 元素（Lord）。</summary>
    internal IEnumerable<XmlElement> Lords => Resolved(lords);

    /// <summary><c>Class</c> 以 MooGirl 时代命名空间开头的元素。</summary>
    internal IReadOnlyList<XmlElement> MooGirlClassElements => mooGirlClassElements;

    /// <summary>文本符合 MooGirl 时代 defName 模式的 <c>def</c> 与 <c>li</c> 叶元素。</summary>
    internal IReadOnlyList<XmlElement> MooGirlLeaves => mooGirlLeaves;

    /// <summary>按原始 <c>Class</c> 值分组的元素（值比较不区分大小写）。</summary>
    internal IReadOnlyDictionary<string, List<XmlElement>> ClassGroups => byClass;

    /// <summary>遍历 <paramref name="game"/> 子树建立索引。</summary>
    /// <param name="names">需要按名收集的元素名。</param>
    /// <param name="classMapper">原始 <c>Class</c> 值 → 类型表映射后的存档类名（未改名时返回规范化的原值）。</param>
    /// <param name="renamedThingDef">ThingDef 旧名 → 新名（未改名时为 null），用于识别 ID 已改写的物品。</param>
    internal static SaveIndex Build(
        XmlElement game,
        IEnumerable<string> names,
        IEnumerable<string> mooGirlTypePrefixes,
        IEnumerable<GlobPattern> mooGirlDefPatterns,
        Func<string, string> classMapper,
        Func<string, string?> renamedThingDef)
    {
        ArgumentNullException.ThrowIfNull(game);
        var index = new SaveIndex(names, mooGirlTypePrefixes, mooGirlDefPatterns, classMapper, renamedThingDef);
        index.Visit(game);
        return index;
    }

    /// <summary>名为 <paramref name="name"/> 的元素；名称必须在建立索引时登记过。</summary>
    internal IEnumerable<XmlElement> ElementsNamed(string name)
    {
        if (!indexedNames.Contains(name))
        {
            throw new InvalidOperationException("Element name '" + name + "' was not registered for the save index; declare it in the step's IndexNames or Consumes.");
        }

        if (!byName.TryGetValue(name, out List<XmlElement>? list))
        {
            return [];
        }

        // 改名后的元素不再属于旧名称；改名产生的新元素已按新名称登记。
        return replacements.Count == 0 ? list : Resolved(list).Where(element => element.Name == name);
    }

    /// <summary>（类型表映射后）<c>Class</c> 为 <paramref name="saveName"/> 的元素。</summary>
    internal IEnumerable<XmlElement> ElementsWithMappedClass(string saveName)
    {
        if (byMappedClass is null)
        {
            byMappedClass = new Dictionary<string, List<XmlElement>>(SaveTypeNames.Comparer);
            var seen = new HashSet<XmlElement>();
            foreach (List<XmlElement> elements in byClass.Values)
            {
                foreach (XmlElement element in Resolved(elements))
                {
                    if (!seen.Add(element) || element.GetAttribute("Class") is not { Length: > 0 } current)
                    {
                        continue;
                    }

                    string mapped = classMapper(current);
                    if (!byMappedClass.TryGetValue(mapped, out List<XmlElement>? target))
                    {
                        target = [];
                        byMappedClass.Add(mapped, target);
                    }

                    target.Add(element);
                }
            }
        }

        return byMappedClass.TryGetValue(saveName, out List<XmlElement>? list) ? Resolved(list) : [];
    }

    /// <summary>存档中 def 原文为 <paramref name="def"/> 的物品（元素已解析）。</summary>
    internal IReadOnlyList<IndexedThing> ThingsWithDef(string def) =>
        thingsByDef.TryGetValue(def, out List<IndexedThing>? list) ? Resolved(list) : [];

    internal bool TryGetThing(XmlElement element, out IndexedThing thing) => thingByElement.TryGetValue(element, out thing);

    internal bool TryGetHediffDef(XmlElement element, [NotNullWhen(true)] out string? def) => hediffDefByElement.TryGetValue(element, out def);

    /// <summary>编辑原语改名元素后调用：旧元素的索引项转给新元素。</summary>
    internal void OnReplaced(XmlElement old, XmlElement created)
    {
        replacements[old] = created;
        if (thingByElement.Remove(old, out IndexedThing thing))
        {
            thingByElement[created] = thing with { Element = created };
        }

        if (hediffDefByElement.Remove(old, out string? def))
        {
            hediffDefByElement[created] = def;
        }

        if (lordSet.Remove(old))
        {
            lordSet.Add(created);
        }

        if (billSet.Remove(old))
        {
            billSet.Add(created);
        }

        Track(created);
    }

    /// <summary><paramref name="element"/> 是否是 <c>lords/li</c>（Lord）。</summary>
    internal bool IsLord(XmlElement element) => lordSet.Contains(element);

    /// <summary>
    /// 收集 <paramref name="root"/> 子树（含自身）中已索引对象的加载 ID：物品的 ThingID、<c>Lord_&lt;loadID&gt;</c>、
    /// <c>Bill_&lt;recipe&gt;_&lt;loadID&gt;</c>。在子树离开文档之后调用也有效（不依赖父节点）。
    /// </summary>
    internal void CollectLoadIds(XmlElement root, ICollection<string> ids)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(ids);
        var pending = new Stack<XmlElement>();
        pending.Push(root);
        while (pending.TryPop(out XmlElement? element))
        {
            if (thingByElement.TryGetValue(element, out IndexedThing thing))
            {
                ids.Add(thing.Id);
            }
            else if (lordSet.Contains(element))
            {
                ids.Add(LordLoadId(element));
            }
            else if (billSet.Contains(element) && BillLoadId(element) is { } bill)
            {
                ids.Add(bill);
            }

            for (XmlNode? child = element.FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is XmlElement childElement)
                {
                    pending.Push(childElement);
                }
            }
        }
    }

    /// <summary>Lord 的加载 ID（<c>Lord.GetUniqueLoadID</c>）；<c>loadID</c> 为默认值 0 时 Scribe 不写这个标签。</summary>
    internal static string LordLoadId(XmlElement lord) => "Lord_" + IntegerOrZero(lord["loadID"]);

    /// <summary>账单的加载 ID（<c>Bill.GetUniqueLoadID</c>）；没有 <c>recipe</c> 时为 null。</summary>
    internal static string? BillLoadId(XmlElement bill) =>
        bill["recipe"]?.InnerText.Trim() is { Length: > 0 } recipe ? "Bill_" + recipe + "_" + IntegerOrZero(bill["loadID"]) : null;

    /// <summary>编辑原语插入新元素后调用。</summary>
    internal void OnAppended(XmlElement created)
    {
        Track(created);
        OnClassChanged(created);
    }

    /// <summary>新容器或属性原语写入 Class 后更新候选索引；选择器仍核实元素当前类名。</summary>
    internal void OnClassChanged(XmlElement element)
    {
        if (element.GetAttribute("Class") is { Length: > 0 } name)
        {
            AddClass(element, name);
            byMappedClass = null;
        }
    }

    /// <summary>编辑原语删除元素后调用。</summary>
    internal void OnRemoved() => RemovedCount++;

    /// <summary>沿改名替换关系解析到当前元素。</summary>
    internal XmlElement Resolve(XmlElement element)
    {
        XmlElement current = element;
        while (replacements.TryGetValue(current, out XmlElement? next))
        {
            current = next;
        }

        return current;
    }

    private IEnumerable<XmlElement> Resolved(List<XmlElement> list)
    {
        if (replacements.Count == 0)
        {
            return list;
        }

        var result = new List<XmlElement>(list.Count);
        var seen = new HashSet<XmlElement>();
        foreach (XmlElement element in list)
        {
            XmlElement resolved = Resolve(element);
            if (seen.Add(resolved))
            {
                result.Add(resolved);
            }
        }

        return result;
    }

    private IReadOnlyList<IndexedThing> Resolved(List<IndexedThing> list) =>
        replacements.Count == 0 ? list : [.. list.Select(thing => thing with { Element = Resolve(thing.Element) })];

    private void Track(XmlElement created)
    {
        if (indexedNames.Contains(created.Name))
        {
            Add(byName, created.Name, created);
        }
    }

    private void Visit(XmlElement element)
    {
        ElementCount++;
        string name = element.Name;
        if (element.HasAttributes && element.GetAttributeNode("Class") is { } classAttribute && classAttribute.Value.Length > 0)
        {
            AddClass(element, classAttribute.Value.Trim());
        }

        if (indexedNames.Contains(name))
        {
            Add(byName, name, element);
        }

        XmlNode? first = element.FirstChild;
        if (first is null)
        {
            return;
        }

        if (first.NextSibling is null && first.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
        {
            VisitLeaf(element, name, first.Value ?? string.Empty);
            return;
        }

        if (name == "li" && element.ParentNode is XmlElement parent)
        {
            switch (parent.Name)
            {
                case "hediffs" when parent.ParentNode is XmlElement { Name: "hediffSet" } && ChildNamed(first, "def") is { } hediffDef:
                    // Hediff.ExposeData 先写 loadID、ageTicks、severity 等值，def 在其后。
                    string def = hediffDef.InnerText;
                    hediffs.Add(new IndexedHediff(element, def));
                    hediffDefByElement[element] = def;
                    break;
                case "bills":
                    bills.Add(element);
                    billSet.Add(element);
                    break;
                case "lords":
                    lords.Add(element);
                    lordSet.Add(element);
                    break;
                default:
                    break;
            }
        }

        XmlNode? firstElement = first;
        while (firstElement is not null && firstElement is not XmlElement) { firstElement = firstElement.NextSibling; }
        if (firstElement is XmlElement { Name: "def" } defElement)
        {
            TryAddThing(element, defElement);
        }

        for (XmlNode? child = first; child is not null; child = child.NextSibling)
        {
            if (child is XmlElement childElement)
            {
                Visit(childElement);
            }
        }
    }

    private void VisitLeaf(XmlElement element, string name, string text)
    {
        if (name is not ("li" or "def"))
        {
            return;
        }

        if (mooGirlExactDefs.Contains(text))
        {
            mooGirlLeaves.Add(element);
            return;
        }

        foreach (GlobPattern pattern in mooGirlDefPatterns)
        {
            if (pattern.IsMatch(text))
            {
                mooGirlLeaves.Add(element);
                return;
            }
        }
    }

    private void TryAddThing(XmlElement element, XmlElement defElement)
    {
        // Thing.ExposeData 先写 def，之后可能有 tickDelta，再写 id。
        XmlNode? sibling = defElement.NextSibling;
        for (int i = 0; i < 3 && sibling is not null; sibling = sibling.NextSibling)
        {
            if (sibling is not XmlElement) { continue; }
            i++;
            if (sibling is XmlElement { Name: "id" } idElement)
            {
                string def = defElement.InnerText;
                string id = idElement.InnerText;
                if (LoadIdTokens.IsThingIdOf(def, id) || (renamedThingDef(def) is { } renamed && LoadIdTokens.IsThingIdOf(renamed, id)))
                {
                    var thing = new IndexedThing(element, def, id);
                    things.Add(thing);
                    thingByElement[element] = thing;
                    if (!thingsByDef.TryGetValue(def, out List<IndexedThing>? list))
                    {
                        list = [];
                        thingsByDef.Add(def, list);
                    }

                    list.Add(thing);
                }

                return;
            }
        }
    }

    private void AddClass(XmlElement element, string value)
    {
        Add(byClass, value, element);
        foreach (string prefix in mooGirlTypePrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                mooGirlClassElements.Add(element);
                break;
            }
        }
    }

    private static XmlElement? ChildNamed(XmlNode first, string name)
    {
        for (XmlNode? node = first; node is not null; node = node.NextSibling)
        {
            if (node is XmlElement element && element.Name == name)
            {
                return element;
            }
        }

        return null;
    }

    private static string IntegerOrZero(XmlElement? element) =>
        element?.InnerText.Trim() is { Length: > 0 } text ? text : "0";

    private static void Add(Dictionary<string, List<XmlElement>> map, string key, XmlElement element)
    {
        if (!map.TryGetValue(key, out List<XmlElement>? list))
        {
            list = [];
            map.Add(key, list);
        }

        list.Add(element);
    }
}
