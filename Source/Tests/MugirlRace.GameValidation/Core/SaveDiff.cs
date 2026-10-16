using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>
/// 比较两份存档中属于被测 Mod 的内容：通常是读入的存档（A）与读档后立即写出的重存档（B，未经过 Tick）。
/// 读档的构建没有读到、读错或换了标签写出的内容都表现为差异。
/// </summary>
/// <remarks>
/// <para>
/// 移植自 hk 的 BlackScience.SmokeTest SaveDiff。比较项只覆盖被测 Mod 拥有的节点（<see cref="SaveDiffScope"/>）：
/// </para>
/// <list type="bullet">
/// <item>本 Mod 的非 Pawn 物品（<c>thing:&lt;id&gt;</c>）：整个节点；组件写在节点里，物品的组成由本 Mod 的 Def 决定。</item>
/// <item>本 Mod 种族的 Pawn：只比较身份（Class、def、kindDef），另外每个本 Mod 组件标签单独一项（<c>thing:&lt;id&gt;/&lt;标签&gt;</c>）；
/// 原版与 HAR 的各 Tracker 不整体比较（它们的读写噪声不属于本 Mod），其中本 Mod 的 Hediff、基因、工作、服装等由下面的项覆盖。</item>
/// <item>其他物品节点里的本 Mod 组件标签（<c>thing:&lt;id&gt;/&lt;标签&gt;</c>）。</item>
/// <item>这些项之外每个 Class 属于本 Mod 的节点（<c>class:…</c>）与每个 def 属于本 Mod 的节点（<c>def:…</c>）。嵌套的本 Mod 物品单独作为比较项。</item>
/// </list>
/// <para>普通叶子文本按 <see cref="SaveXml.SameValue"/> 精确比较，颜色变化只按已核实的上下文解释。只读原版 XML 形状，不引用 Mod 类型；-Plumbing 构建也能使用。</para>
/// </remarks>
internal sealed class SaveDiff
{
    private const int MaxValueLength = 160;

    /// <summary>本 Mod 种族 Pawn 的身份项比较的子元素。</summary>
    private static readonly string[] IdentityLabels = ["def", "kindDef"];

    private readonly SaveDiffScope scope;
    private readonly HashSet<string> identityItems = new(StringComparer.Ordinal);
    private readonly HashSet<XElement> normalizedColorLeaves = [];
    private readonly Dictionary<XElement, XElement> comparedElements = [];

    public SaveDiff(SaveDiffScope scope)
    {
        this.scope = scope;
    }

    /// <summary>上一次比较中 A 的各类比较项数量，用于报告。</summary>
    public Dictionary<string, int> ItemCounts { get; } = new(StringComparer.Ordinal);

    /// <summary>兼容既有报告结构；比较器不再凭标签或日志键自动消除读档差异。</summary>
    public List<string> LoadResets { get; } = [];

    public List<SaveDifference> Compare(XDocument a, XDocument b)
    {
        identityItems.Clear();
        comparedElements.Clear();
        Dictionary<string, XElement> itemsA = Items(a);
        Dictionary<string, XElement> itemsB = Items(b);
        ItemCounts.Clear();
        LoadResets.Clear();
        CollectColorableNormalizations(a, b);
        foreach (string key in itemsA.Keys)
        {
            string kind = key[..key.IndexOf(':', StringComparison.Ordinal)];
            if (kind == "thing")
            {
                kind = key.Contains('/', StringComparison.Ordinal) ? "comp-label" : identityItems.Contains(key) ? "mod-pawn" : "thing";
            }

            ItemCounts[kind] = ItemCounts.GetValueOrDefault(kind) + 1;
        }

        var differences = new List<SaveDifference>();
        foreach ((string key, XElement elementA) in itemsA.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!itemsB.TryGetValue(key, out XElement? elementB))
            {
                differences.Add(new SaveDifference("missing", key, string.Empty, Short(elementA.ToString(SaveOptions.DisableFormatting)), null) { BeforeElement = elementA });
                continue;
            }

            if ((key.StartsWith("thing:", StringComparison.Ordinal) && !key.Contains('/', StringComparison.Ordinal)
                    || elementA.Name.LocalName == "li" && (key.StartsWith("class:", StringComparison.Ordinal) || key.StartsWith("def:", StringComparison.Ordinal)))
                && HolderPosition(elementA) is { } positionA && HolderPosition(elementB) is { } positionB && positionA != positionB)
            {
                differences.Add(new SaveDifference("membership", key, "@holder", positionA, positionB) { BeforeElement = elementA, AfterElement = elementB });
            }

            if (identityItems.Contains(key))
            {
                CompareIdentity(key, elementA, elementB, differences);
                continue;
            }

            CompareElements(key, elementA, elementB, string.Empty, differences);
        }

        foreach ((string key, XElement elementB) in itemsB.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!itemsA.ContainsKey(key))
            {
                differences.Add(new SaveDifference("added", key, string.Empty, null, Short(elementB.ToString(SaveOptions.DisableFormatting))) { AfterElement = elementB });
            }
        }

        return differences;
    }

    private static string? HolderPosition(XElement thing)
    {
        if (thing.Parent is not { } parent)
        {
            return null;
        }

        XElement? owner = thing.Ancestors().FirstOrDefault(SaveXml.IsThing);
        IEnumerable<XElement> chain = thing.AncestorsAndSelf().TakeWhile(e => e != owner).Reverse();
        if (owner is null && parent.Name.LocalName == "things")
        {
            XElement? map = parent.Parent;
            return "map:" + (map?.Element("uniqueID")?.Value ?? map?.Element("index")?.Value ?? "unknown") + "/things";
        }

        string path = string.Join('/', chain.Select(e => e.Name.LocalName + "[" + (e.ElementsBeforeSelf(e.Name).Count() + 1).ToString(CultureInfo.InvariantCulture) + "]"));
        return (owner is null ? "document:" : "thing:" + SaveXml.Id(owner) + "/") + path;
    }

    /// <summary>只解释已核实的重复 CompColorable 读写：首项严格不变，后续实例读取首项后写出同值；原始差异始终保留。</summary>
    public string? ExplainColorableNormalization(SaveDifference difference)
        => difference.Kind is "added" or "changed" && difference.AfterElement is { } leaf && normalizedColorLeaves.Contains(leaf)
            ? "duplicate vanilla CompColorable read the first color/colorActive; primary values are unchanged and all saved duplicates equal them"
            : null;

    public string? ExplainForbiddableNormalization(SaveDifference difference)
    {
        if (difference.Kind != "added" || difference.AfterElement is not { Name.LocalName: "forbidden" } leaf
            || leaf.Parent is not { } after || SaveXml.ClassOf(after) is not ("Apparel" or "RimWorld.Apparel")
            || SaveXml.Def(after) != DuplicateForbiddableEvidence.Definition || SaveXml.Id(after) is not { } id
            || scope.VerifiedDuplicateForbiddableThingIds?.Contains(id) != true || after.Parent?.Name.LocalName != "things"
            || after.Parent.Parent?.Parent?.Name.LocalName != "maps"
            || after.Document is not { } afterDoc || !ReferenceEquals(after.Parent.Parent.Parent.Parent, SaveXml.Game(afterDoc))) { return null; }
        XElement? before = comparedElements.FirstOrDefault(pair => ReferenceEquals(pair.Value, after)).Key;
        if (before is null || SaveXml.ClassOf(before) != SaveXml.ClassOf(after) || SaveXml.Id(before) != id || SaveXml.Def(before) != SaveXml.Def(after)
            || before.Document is not { } beforeDoc || !ReferenceEquals(before.Parent?.Parent?.Parent?.Parent, SaveXml.Game(beforeDoc))) { return null; }
        XElement[] a = [.. before.Elements("forbidden")];
        XElement[] b = [.. after.Elements("forbidden")];
        static bool ExactTrue(XElement field) => !field.HasAttributes && field.Nodes().ToArray() is [XText text] && text is not XCData && text.Value == "True";
        return a.Length == 1 && b.Length == 2 && ReferenceEquals(b[1], leaf) && ExactTrue(a[0]) && b.All(ExactTrue)
            ? "two verified vanilla CompForbiddable instances read the unique first True leaf and each write True; primary value, full roster and actual component state are unchanged"
            : null;
    }

    /// <summary>原版 Verb.currentTarget 的 null 读成 Invalid，再保存时因等于该字段默认值而省略。</summary>
    public string? ExplainVerbTargetDefault(SaveDifference difference)
    {
        if (difference.Kind != "missing" || difference.BeforeElement is not { Name.LocalName: "currentTarget" } leaf
            || leaf.HasAttributes || leaf.HasElements || leaf.Value != "null" || leaf.Parent is not { Name.LocalName: "li" } verb
            || verb.Parent?.Name.LocalName != "verbs" || verb.Parent.Parent?.Name.LocalName != "verbTracker"
            || verb.Attribute("Class")?.Value is not { } type || scope.DefaultTargetVerbClasses?.Contains(type) != true
            || !comparedElements.TryGetValue(verb, out XElement? after) || after.Attribute("Class")?.Value != type
            || verb.Element("loadID")?.Value is not { Length: > 0 } id || after.Element("loadID")?.Value != id || after.Element("currentTarget") is not null)
        {
            return null;
        }

        return "vanilla Verb.currentTarget: null and missing both read as LocalTargetInfo.Invalid; saving omits this exact default";
    }

    /// <summary>仅解释原版已证明的两个单向空列表初始化，并要求运行时 Def 类型证据。</summary>
    public string? ExplainCollectionInitialization(SaveDifference difference)
    {
        if (difference.Kind != "nullness" || difference.BeforeElement is not { } before || difference.AfterElement is not { } after
            || !SaveXml.HasNullAttribute(before) || before.Attributes().Count() != 1 || before.HasElements || before.Value.Length != 0
            || after.HasAttributes || after.HasElements || after.Value.Length != 0 || before.Name != after.Name
            || before.Parent is not { } oldOwner || after.Parent is not { } newOwner || !SaveXml.IsThing(oldOwner) || !SaveXml.IsThing(newOwner)
            || SaveXml.Id(oldOwner) != SaveXml.Id(newOwner) || SaveXml.Def(oldOwner) is not { } def || SaveXml.Def(newOwner) != def)
        {
            return null;
        }

        return before.Name.LocalName switch
        {
            "abilities" when scope.EmptyAbilityApparelDefs?.Contains(def) == true => "Apparel.PostLoadInit initializes its null Deep abilities list from the runtime Def's empty abilities list",
            "thingsIgnoredByExplosion" when scope.ExplosiveReferenceListDefs?.Contains(def) == true => "CompExplosive Reference list: CrossRefHandler.TakeResolvedRefList converts null IDs to an empty list",
            _ => null,
        };
    }

    private void CollectColorableNormalizations(XDocument before, XDocument after)
    {
        normalizedColorLeaves.Clear();
        if (scope.DuplicateColorableComps is not { Count: > 0 } counts)
        {
            return;
        }

        Dictionary<string, XElement> oldThings = before.Descendants().Where(SaveXml.IsThing)
            .GroupBy(e => SaveXml.Id(e)!, StringComparer.Ordinal).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        foreach (XElement thing in after.Descendants().Where(SaveXml.IsThing))
        {
            if (SaveXml.Def(thing) is not { } def || !counts.TryGetValue(def, out int count) || count < 2
                || !oldThings.TryGetValue(SaveXml.Id(thing)!, out XElement? old) || SaveXml.Def(old) != def)
            {
                continue;
            }

            List<XElement> candidates = [];
            bool matches = true;
            foreach (string label in new[] { "color", "colorActive" })
            {
                List<XElement> a = [.. old.Elements(label)];
                List<XElement> b = [.. thing.Elements(label)];
                if (a.Count == 0 || a.Count > count || b.Count != count || a[0].Value != b[0].Value
                    || b.Any(e => e.HasElements || e.HasAttributes || e.Value != b[0].Value))
                {
                    matches = false;
                    break;
                }

                candidates.AddRange(b.Skip(1));
            }

            if (matches)
            {
                normalizedColorLeaves.UnionWith(candidates);
            }
        }
    }

    /// <summary>两份存档中当前工作不同的 Pawn（按 ThingID，含原版 Pawn）：区分读档因本 Mod 丢掉的工作与原版读档本来就会重置的工作。</summary>
    public List<string> JobChanges(XDocument a, XDocument b)
    {
        Dictionary<string, (string Def, string Job)> before = CurrentJobs(a);
        Dictionary<string, (string Def, string Job)> after = CurrentJobs(b);
        var changes = new List<string>();
        foreach ((string id, (string def, string job)) in before.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            string jobAfter = after.TryGetValue(id, out (string Def, string Job) b2) ? b2.Job : "(pawn missing)";
            if (jobAfter != job)
            {
                changes.Add($"{(scope.ModThingDefNames.Contains(def) ? "mod" : "vanilla")} pawn {id} ({def}): {job} -> {jobAfter}");
            }
        }

        return changes;
    }

    /// <summary>比较项数量的简短汇总。</summary>
    public string DescribeCounts()
    {
        var text = new StringBuilder();
        foreach ((string kind, int count) in ItemCounts.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            text.Append(kind).Append('=').Append(count.ToString(CultureInfo.InvariantCulture)).Append(' ');
        }

        return text.ToString().TrimEnd();
    }

    private Dictionary<string, XElement> Items(XDocument document)
    {
        var items = new Dictionary<string, XElement>(StringComparer.Ordinal);
        if (SaveXml.Game(document) is { } game)
        {
            VisitChildren(game, null, "game", insideItem: false, items);
        }

        return items;
    }

    // ownerId 是最近的外层物品（任何物品，不只本 Mod 的）的 id。insideItem：已在一个整体比较的项内，只收集嵌套的本 Mod 物品。
    private void Visit(XElement element, string? ownerId, string parentPath, bool insideItem, Dictionary<string, XElement> items)
    {
        string path = parentPath + "/" + element.Name.LocalName;
        string? id = SaveXml.Id(element);
        string? def = SaveXml.Def(element);
        if (element.Name.LocalName == "li" && element.Parent?.Name.LocalName == "lords"
            && element.Element("lordJob") is { } lordJob && SaveXml.IsModClass(SaveXml.ClassOf(lordJob)))
        {
            // LordJob 拥有整个状态图；只比较其 Class 子节点会漏掉 curLordToilIdx、原版 LordToilData 和触发器状态。
            string identity = element.Element("loadID")?.Value.Trim() ?? SaveXml.ClassOf(lordJob)!;
            Add(items, "lord:" + identity, element);
            VisitChildren(element, ownerId, path, insideItem: true, items);
            return;
        }

        if (IsModThing(element))
        {
            if (SaveXml.IsPawn(element) && !insideItem)
            {
                // 本 Mod 种族的 Pawn：身份一项，组件标签各一项，其余（原版与 HAR 的 Tracker）只往下找本 Mod 的节点。
                identityItems.Add(Add(items, "thing:" + id, element));
                VisitThingChildren(element, id!, def!, path, insideItem: false, items);
                return;
            }

            Add(items, "thing:" + id, element);
            VisitChildren(element, id, path, insideItem: true, items);
            return;
        }

        string? className = SaveXml.ClassOf(element);
        string owner = ownerId ?? parentPath;
        if (id is not null && def is not null && !(!insideItem && SaveXml.IsModClass(className)))
        {
            VisitThingChildren(element, id, def, path, insideItem, items);
            return;
        }

        if (!insideItem && SaveXml.IsModClass(className))
        {
            Add(items, $"class:{owner}/{element.Name.LocalName}[{className}]", element);
            VisitChildren(element, ownerId, path, insideItem: true, items);
            return;
        }

        if (!insideItem && def is not null && scope.ModDefNames.Contains(def))
        {
            Add(items, $"def:{owner}/{element.Parent?.Name.LocalName}/{element.Name.LocalName}[{def}]", element);
            VisitChildren(element, ownerId, path, insideItem: true, items);
            return;
        }

        VisitChildren(element, ownerId, path, insideItem, items);
    }

    // 物品节点的子元素：本 Mod 组件标签单独成项（项内只再找嵌套的本 Mod 物品），其余照常往下找。
    private void VisitThingChildren(XElement thing, string id, string def, string path, bool insideItem, Dictionary<string, XElement> items)
    {
        foreach (XElement child in thing.Elements())
        {
            if (!insideItem && scope.IsModCompLabel(def, child.Name.LocalName))
            {
                Add(items, $"thing:{id}/{child.Name.LocalName}", child);
                VisitChildren(child, id, path + "/" + child.Name.LocalName, insideItem: true, items);
                continue;
            }

            Visit(child, id, path, insideItem, items);
        }
    }

    private void VisitChildren(XElement element, string? ownerId, string path, bool insideItem, Dictionary<string, XElement> items)
    {
        foreach (XElement child in element.Elements())
        {
            Visit(child, ownerId, path, insideItem, items);
        }
    }

    private static string Add(Dictionary<string, XElement> items, string key, XElement element)
    {
        string unique = key;
        for (int i = 2; items.ContainsKey(unique); i++)
        {
            unique = string.Create(CultureInfo.InvariantCulture, $"{key}#{i}");
        }

        items[unique] = element;
        return unique;
    }

    // 本 Mod 种族 Pawn 的身份：Class 与 def、kindDef 两个引用。
    private static void CompareIdentity(string item, XElement a, XElement b, List<SaveDifference> differences)
    {
        string classA = SaveXml.ClassOf(a) ?? string.Empty;
        string classB = SaveXml.ClassOf(b) ?? string.Empty;
        if (classA != classB)
        {
            differences.Add(new SaveDifference("changed", item, "@Class", classA, classB) { BeforeElement = a, AfterElement = b });
        }

        foreach (string label in IdentityLabels)
        {
            string? valueA = a.Element(label)?.Value.Trim();
            string? valueB = b.Element(label)?.Value.Trim();
            if (valueA != valueB)
            {
                differences.Add(new SaveDifference("changed", item, "/" + label, valueA ?? "(none)", valueB ?? "(none)")
                {
                    BeforeElement = a.Element(label) ?? a,
                    AfterElement = b.Element(label) ?? b,
                });
            }
        }
    }

    private void CompareElements(string item, XElement a, XElement b, string path, List<SaveDifference> differences)
    {
        comparedElements[a] = b;
        string classA = SaveXml.ClassOf(a) ?? string.Empty;
        string classB = SaveXml.ClassOf(b) ?? string.Empty;
        if (classA != classB)
        {
            differences.Add(new SaveDifference("changed", item, path + "@Class", classA, classB) { BeforeElement = a, AfterElement = b });
        }

        foreach (XName name in a.Attributes().Select(x => x.Name).Concat(b.Attributes().Select(x => x.Name)).Distinct()
            .Where(n => n.LocalName != "Class" && (n.LocalName != "IsNull" || SaveXml.HasNullAttribute(a) == SaveXml.HasNullAttribute(b))))
        {
            if (a.Attribute(name)?.Value != b.Attribute(name)?.Value)
            {
                differences.Add(new SaveDifference("attribute", item, path + "@" + name, a.Attribute(name)?.Value, b.Attribute(name)?.Value)
                    { BeforeElement = a, AfterElement = b });
            }
        }
        if ((a.HasElements || b.HasElements) && SaveXml.HasNullAttribute(a) != SaveXml.HasNullAttribute(b))
        {
            differences.Add(new SaveDifference("nullness", item, path, a.Attribute("IsNull")?.Value, b.Attribute("IsNull")?.Value)
                { BeforeElement = a, AfterElement = b });
        }

        if (!a.HasElements && !b.HasElements)
        {
            string valueA = a.Value;
            string valueB = b.Value;
            bool nullA = SaveXml.HasNullAttribute(a);
            bool nullB = SaveXml.HasNullAttribute(b);
            if (!SaveXml.SameValue(valueA, valueB))
            {
                differences.Add(new SaveDifference("changed", item, path, Short(valueA), Short(valueB)) { BeforeElement = a, AfterElement = b });
            }
            else if (nullA != nullB)
            {
                differences.Add(new SaveDifference("nullness", item, path, nullA ? "null" : "empty", nullB ? "null" : "empty") { BeforeElement = a, AfterElement = b });
            }

            return;
        }

        Dictionary<string, XElement> childrenA = Children(a);
        Dictionary<string, XElement> childrenB = Children(b);
        foreach ((string key, XElement childA) in childrenA)
        {
            if (childrenB.TryGetValue(key, out XElement? childB))
            {
                if (IsModThing(childA) || IsModThing(childB))
                {
                    string? beforeId = SaveXml.Id(childA);
                    string? afterId = SaveXml.Id(childB);
                    if (beforeId != afterId || SaveXml.Def(childA) != SaveXml.Def(childB))
                    {
                        differences.Add(new SaveDifference("membership", item, path + "/" + key, beforeId, afterId) { BeforeElement = childA, AfterElement = childB });
                    }
                }
                else
                {
                    CompareElements(item, childA, childB, path + "/" + key, differences);
                }
            }
            else
            {
                differences.Add(new SaveDifference("missing", item, path + "/" + key, Short(childA.ToString(SaveOptions.DisableFormatting)), null) { BeforeElement = childA });
            }
        }

        foreach ((string key, XElement childB) in childrenB)
        {
            if (!childrenA.ContainsKey(key))
            {
                differences.Add(new SaveDifference("added", item, path + "/" + key, null, Short(childB.ToString(SaveOptions.DisableFormatting))) { AfterElement = childB });
            }
        }
    }

    private bool IsModThing(XElement element)
        => element.Element("id") is not null && element.Element("def")?.Value is { } def && scope.ModThingDefNames.Contains(def.Trim());

    // 普通字段按标签配对；所有列表按位置配对，不把文本或对象 ID 当作无序集合键。
    private static Dictionary<string, XElement> Children(XElement element)
    {
        var children = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (XElement child in element.Elements())
        {
            string name = child.Name.LocalName;
            string key = name;
            int count = occurrences.GetValueOrDefault(key);
            occurrences[key] = count + 1;
            children[count == 0 ? key : string.Create(CultureInfo.InvariantCulture, $"{key}#{count + 1}")] = child;
        }

        return children;
    }

    private static string Short(string value)
    {
        string flat = value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return flat.Length <= MaxValueLength ? flat : flat[..MaxValueLength] + "…";
    }

    private static Dictionary<string, (string Def, string Job)> CurrentJobs(XDocument document)
    {
        var jobs = new Dictionary<string, (string Def, string Job)>(StringComparer.Ordinal);
        foreach (XElement tracker in document.Descendants("jobs"))
        {
            if (tracker.Parent?.Element("id")?.Value is { } id && tracker.Parent.Element("def")?.Value is { } def)
            {
                jobs.TryAdd(id, (def, tracker.Element("curJob")?.Element("def")?.Value ?? "none"));
            }
        }

        return jobs;
    }
}
