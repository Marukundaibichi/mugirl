using System.Globalization;
using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>从存档 XML 中读出的、与被测 Mod 有关的“事实”：键值对，键按序排列，值保留原始文本和数值精度。</summary>
/// <remarks>
/// <para>
/// 只读原版 XML 形状，不加载存档、不引用 Mod 类型；读取名由 <see cref="SaveFactQueries"/> 给出（夹具用旧名，重存档用换成新名的查询）。
/// 键由 "/" 分隔的段组成，名字段的种类由位置决定，所以 <see cref="Translate"/> 能按根类型与 owner 把旧名事实换成新名：
/// </para>
/// <list type="bullet">
/// <item><c>save/tick</c>；<c>class/&lt;类型&gt;</c>：该 Class 的节点数；<c>thing/&lt;ThingDef&gt;</c>：该 Def 的物品数（地图、容器、世界 Pawn）；
/// <c>quest/&lt;QuestScriptDef&gt;</c>：任务数；<c>lord/&lt;LordJob 类型&gt;</c>：Lord 数。</item>
/// <item><c>pawn/&lt;ThingID&gt;/…</c>：雪牛娘种族、或带本 Mod 的 Hediff/服装/基因的 Pawn——当前工作（job、driver）、
/// Hediff（<c>hediff/&lt;HediffDef&gt;</c> 数量、<c>/severity</c>、<c>/@class</c> 与组件的标量 <c>/&lt;标签&gt;</c>）、穿着的本 Mod 服装、奶量、
/// 武器轮盘、绳索、异种型、基因、能力、特性。</item>
/// <item><c>restraint/&lt;ThingID&gt;/…</c>：束缚服装的 Def、上锁标志、锁次数、破解标志与计时（没有写出时取构建的默认值）；<c>restraints/…</c>：汇总。</item>
/// <item><c>component/&lt;类型&gt;[@map&lt;n&gt;]/&lt;标签&gt;</c>：本 Mod 的 GameComponent/MapComponent 的直接标量子元素；列表记为 <c>…/count</c>。</item>
/// </list>
/// <para>事实摘要只记录正的集合计数；它不证明 null、空值或缺失等价，这些状态必须由原始 XML 差异比较验证。</para>
/// </remarks>
internal sealed class SaveFacts
{
    private SaveFacts(SortedDictionary<string, string> values)
    {
        Values = values;
    }

    public SortedDictionary<string, string> Values { get; }

    public static SaveFacts Read(XDocument document, SaveFactQueries queries)
    {
        var facts = new SortedDictionary<string, string>(StringComparer.Ordinal);
        XElement? game = SaveXml.Game(document);
        if (game is null)
        {
            return new SaveFacts(facts);
        }

        if (game.Element("tickManager")?.Element("ticksGame")?.Value is { } tick)
        {
            facts["save/tick"] = tick.Trim();
        }

        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var restraints = new RestraintTally();
        foreach (XElement element in game.Descendants())
        {
            if (SaveXml.ClassOf(element) is { } className && SaveXml.IsModClass(className))
            {
                Count(counts, "class/" + className);
                if (element.Name.LocalName == "lordJob" && element.Parent?.Parent?.Name.LocalName == "lords")
                {
                    Count(counts, "lord/" + className);
                }
            }

            if (element.Name.LocalName == "root" && element.Parent?.Parent?.Name.LocalName == "quests" && element.Value.Trim() is { Length: > 0 } questRoot
                && queries.ModDefs.Contains(questRoot))
            {
                Count(counts, "quest/" + questRoot);
            }

            if (!SaveXml.IsThing(element) || SaveXml.Def(element) is not { } def || SaveXml.Id(element) is not { } id)
            {
                continue;
            }

            if (queries.ModThingDefs.Contains(def))
            {
                Count(counts, "thing/" + def);
            }

            if (SaveXml.IsPawn(element))
            {
                ReadPawn(element, id, def, queries, facts);
            }

            if (queries.RestraintDefaults.TryGetValue(def, out IReadOnlyDictionary<string, string>? defaults))
            {
                ReadRestraint(element, id, def, defaults, queries, facts, restraints);
            }
        }

        foreach ((string key, int count) in counts)
        {
            facts[key] = count.ToString(CultureInfo.InvariantCulture);
        }

        restraints.WriteTo(facts);
        ReadComponents(game.Element("components"), null, facts, queries.NestedComponentFacts);
        int mapIndex = 0;
        foreach (XElement map in game.Element("maps")?.Elements("li") ?? [])
        {
            ReadComponents(map.Element("components"), "@map" + mapIndex.ToString(CultureInfo.InvariantCulture), facts, queries.NestedComponentFacts);
            mapIndex++;
        }

        return new SaveFacts(facts);
    }

    /// <summary>
    /// 把旧名事实（夹具清单中、用 <see cref="SaveFactQueries.Legacy"/> 读出的）换成新名：类型与各根类型的 Def 按清单映射，ThingID 按 ThingDef 映射，
    /// 组件与 Hediff 的标签按 owner 的标签行（<paramref name="context"/> 判断 Def 带有的组件）。属于被删除或被消费的名字的事实不再期待，计入
    /// <paramref name="dropped"/>；同一新键的计数事实相加。值中的 Def 名只在位置明确时翻译，其他值由比较方用 <see cref="SaveNameMap.ExplainsValueChange"/> 解释。
    /// </summary>
    public static SortedDictionary<string, string> Translate(IReadOnlyDictionary<string, string> legacy, SaveNameMap map, ISaveOwnerContext context, out List<string> dropped)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        dropped = [];
        foreach ((string key, string value) in legacy)
        {
            if (TranslateFact(key, value, legacy, map, context) is not { } translated)
            {
                dropped.Add(key);
                continue;
            }

            if (result.TryGetValue(translated.Key, out string? existing) && IsCountKey(key)
                && int.TryParse(existing, NumberStyles.Integer, CultureInfo.InvariantCulture, out int a)
                && int.TryParse(translated.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int b))
            {
                result[translated.Key] = (a + b).ToString(CultureInfo.InvariantCulture);
                continue;
            }

            result[translated.Key] = translated.Value;
        }

        return result;
    }

    private static (string Key, string Value)? TranslateFact(string key, string value, IReadOnlyDictionary<string, string> legacy, SaveNameMap map, ISaveOwnerContext context)
    {
        string[] s = key.Split('/');
        switch (s[0])
        {
            case "save":
            case "restraints":
                return (key, value);
            case "class" when s.Length == 2:
                return map.Type(s[1]) is { } type ? ("class/" + type, value) : null;
            case "lord" when s.Length == 2:
                return map.Type(s[1]) is { } lordType && map.LordPolicy(s[1]) != "removeLord" ? ("lord/" + lordType, value) : null;
            case "thing" when s.Length == 2:
                return map.Def("ThingDef", s[1]) is { } thing ? ("thing/" + thing, value) : null;
            case "quest" when s.Length == 2:
                return map.Def("QuestScriptDef", s[1]) is { } quest ? ("quest/" + quest, value) : null;
            case "restraint" when s.Length == 3:
                string restraint = "restraint/" + map.TranslateId(s[1]) + "/" + s[2];
                return s[2] == "def" ? map.Def("ThingDef", value) is { } restraintDef ? (restraint, restraintDef) : null : (restraint, value);
            case "pawn" when s.Length >= 3:
                return TranslatePawnFact(s, value, legacy, map, context);
            case "component" when s.Length >= 3:
                return TranslateComponentFact(s, value, map);
            default:
                return (key, value);
        }
    }

    private static (string Key, string Value)? TranslatePawnFact(string[] s, string value, IReadOnlyDictionary<string, string> legacy, SaveNameMap map, ISaveOwnerContext context)
    {
        string prefix = "pawn/" + map.TranslateId(s[1]) + "/";
        string rest = string.Join('/', s.Skip(2));
        switch (s[2])
        {
            case "def":
                return map.Def("ThingDef", value) is { } race ? (prefix + "def", race) : null;
            case "job":
                return value == "none" ? (prefix + "job", value) : map.Def("JobDef", value) is { } job ? (prefix + "job", job) : null;
            case "driver":
                return map.Type(value) is { } driver ? (prefix + "driver", driver) : null;
            case "xenotype":
                return map.Def("XenotypeDef", value) is { } xenotype ? (prefix + "xenotype", xenotype) : null;
            case "roping" when s.Length == 4 && s[3] == "ropedTo":
                return (prefix + rest, map.TranslateId(value));
            case "gene" when s.Length == 4:
                return Renamed(prefix + "gene/", map.Def("GeneDef", s[3]), value);
            case "ability" when s.Length == 4:
                return Renamed(prefix + "ability/", map.Def("AbilityDef", s[3]), value);
            case "trait" when s.Length == 4:
                return Renamed(prefix + "trait/", map.Def("TraitDef", s[3]), value);
            case "apparel" when s.Length == 4:
                return Renamed(prefix + "apparel/", map.Def("ThingDef", s[3]), value);
            case "hediff" when s.Length >= 4:
                if (map.Def("HediffDef", s[3]) is not { } hediff)
                {
                    return null;
                }

                string hediffPrefix = prefix + "hediff/" + hediff;
                if (s.Length == 4)
                {
                    return (hediffPrefix, value);
                }

                string label = s[4];
                if (label is "severity")
                {
                    return (hediffPrefix + "/severity", value);
                }

                if (label is "@class")
                {
                    return map.Type(value) is { } hediffClass ? (hediffPrefix + "/@class", hediffClass) : null;
                }

                // 组件标签：HediffComp(组件) 按旧 Def 的组件判断，Class(…) 按该 Hediff 的类判断。
                string? legacyClass = legacy.GetValueOrDefault("pawn/" + s[1] + "/hediff/" + s[3] + "/@class");
                string? mappedClass = legacyClass is null ? null : map.Type(legacyClass);
                SaveLabelFate fate = map.LabelFate(label, owner => owner.HediffCompClass is { } comp
                    ? context.DefHasComp("HediffDef", s[3], comp)
                    : mappedClass is not null && owner.ClassNames.Contains(mappedClass, StringComparer.Ordinal));
                return fate.Kind switch
                {
                    SaveLabelFateKind.Renamed => (hediffPrefix + "/" + fate.NewLabel, value),
                    SaveLabelFateKind.Dropped or SaveLabelFateKind.Consumed => null,
                    _ => (hediffPrefix + "/" + label, value),
                };
            default:
                return (prefix + rest, value);
        }
    }

    private static (string Key, string Value)? TranslateComponentFact(string[] s, string value, SaveNameMap map)
    {
        int at = s[1].IndexOf('@', StringComparison.Ordinal);
        string legacyType = at < 0 ? s[1] : s[1][..at];
        string suffix = at < 0 ? string.Empty : s[1][at..];
        if (map.Type(legacyType) is not { } type)
        {
            return null;
        }

        string prefix = "component/" + type + suffix + "/";
        if (s[2] == "present")
        {
            return (prefix + "present", value);
        }

        SaveLabelFate fate = map.LabelFate(s[2], owner => owner.ClassNames.Contains(type, StringComparer.Ordinal));
        string tail = s.Length > 3 ? "/" + string.Join('/', s.Skip(3)) : string.Empty;
        return fate.Kind switch
        {
            SaveLabelFateKind.Renamed => (prefix + fate.NewLabel + tail, value),
            SaveLabelFateKind.Dropped or SaveLabelFateKind.Consumed => null,
            _ => (prefix + s[2] + tail, value),
        };
    }

    private static (string Key, string Value)? Renamed(string prefix, string? name, string value) => name is null ? null : (prefix + name, value);

    private static bool IsCountKey(string key)
        => key.StartsWith("class/", StringComparison.Ordinal) || key.StartsWith("thing/", StringComparison.Ordinal)
            || key.StartsWith("quest/", StringComparison.Ordinal) || key.StartsWith("lord/", StringComparison.Ordinal)
            || (key.StartsWith("pawn/", StringComparison.Ordinal) && key.Contains("/apparel/", StringComparison.Ordinal));

    private static void Count(SortedDictionary<string, int> counts, string key) => counts[key] = counts.GetValueOrDefault(key) + 1;

    private static void ReadRestraint(XElement element, string id, string def, IReadOnlyDictionary<string, string> defaults, SaveFactQueries queries,
        SortedDictionary<string, string> facts, RestraintTally tally)
    {
        string prefix = "restraint/" + id + "/";
        facts[prefix + "def"] = def;
        foreach ((string key, string? label) in queries.RestraintLabels)
        {
            string? written = label is null ? null : element.Element(label)?.Value;
            string? value = written is not null ? SaveXml.Normalize(written) : defaults.GetValueOrDefault(key);
            if (value is not null)
            {
                facts[prefix + key] = value;
            }
        }

        tally.Add(facts.GetValueOrDefault(prefix + SaveFactQueries.IsLockedKey), facts.GetValueOrDefault(prefix + SaveFactQueries.LockCountKey),
            defaults.GetValueOrDefault(SaveFactQueries.LockCountKey), facts.GetValueOrDefault(prefix + SaveFactQueries.TimerKey), defaults.GetValueOrDefault(SaveFactQueries.TimerKey));
    }

    /// <summary>同一 Pawn 只读一次（世界 Pawn 列表与地图中不会重复写同一个 Pawn 的完整节点）。</summary>
    private static void ReadPawn(XElement pawn, string id, string def, SaveFactQueries queries, SortedDictionary<string, string> facts)
    {
        List<XElement> hediffs = [.. pawn.Element("healthTracker")?.Element("hediffSet")?.Element("hediffs")?.Elements("li")
            .Where(h => SaveXml.Def(h) is { } d && queries.ModDefs.Contains(d)) ?? []];
        List<XElement> apparel = [.. pawn.Element("apparel")?.Element("wornApparel")?.Element("innerList")?.Elements("li")
            .Where(a => SaveXml.Def(a) is { } d && queries.ModThingDefs.Contains(d)) ?? []];
        XElement? genes = pawn.Element("genes");
        List<string> modGenes = [.. (genes?.Element("xenogenes")?.Elements("li") ?? []).Concat(genes?.Element("endogenes")?.Elements("li") ?? [])
            .Select(SaveXml.Def).OfType<string>().Where(queries.ModDefs.Contains)];
        string? xenotype = genes?.Element("xenotype")?.Value.Trim();
        bool relevant = queries.ModThingDefs.Contains(def) || hediffs.Count > 0 || apparel.Count > 0 || modGenes.Count > 0
            || (xenotype is not null && queries.ModDefs.Contains(xenotype));
        string prefix = "pawn/" + id + "/";
        if (!relevant || facts.ContainsKey(prefix + "def"))
        {
            return;
        }

        facts[prefix + "def"] = def;
        XElement? jobs = pawn.Element("jobs");
        facts[prefix + "job"] = jobs?.Element("curJob")?.Element("def")?.Value.Trim() ?? "none";
        if (jobs?.Element("curDriver") is { } driver && SaveXml.ClassOf(driver) is { } driverClass)
        {
            facts[prefix + "driver"] = driverClass;
        }

        foreach (IGrouping<string, XElement> group in hediffs.GroupBy(h => SaveXml.Def(h)!, StringComparer.Ordinal))
        {
            string key = prefix + "hediff/" + group.Key;
            facts[key] = group.Count().ToString(CultureInfo.InvariantCulture);
            XElement first = group.First();
            facts[key + "/@class"] = SaveXml.ClassOf(first) ?? "-";
            if (first.Element("severity") is { } severity)
            {
                facts[key + "/severity"] = SaveXml.Normalize(severity.Value);
            }

            foreach (XElement child in first.Elements().Where(c => SaveXml.IsLeaf(c) && !queries.VanillaHediffLabels.Contains(c.Name.LocalName)))
            {
                facts[key + "/" + child.Name.LocalName] = SaveXml.Normalize(child.Value);
            }
        }

        foreach (IGrouping<string, XElement> group in apparel.GroupBy(a => SaveXml.Def(a)!, StringComparer.Ordinal))
        {
            facts[prefix + "apparel/" + group.Key] = group.Count().ToString(CultureInfo.InvariantCulture);
        }

        if (queries.MilkFullnessLabel is { } milkLabel && pawn.Element(milkLabel) is { } milk)
        {
            facts[prefix + "milk/fullness"] = SaveXml.Normalize(milk.Value);
        }

        if (queries.WheelSlotsLabel is { } slotsLabel && pawn.Element(slotsLabel) is { } slots)
        {
            Positive(facts, prefix + "wheel/slots", slots.Elements("li").Count(li => queries.WheelRecords
                ? li.Element("weapon") is { } weapon && !SaveXml.IsNullReference(weapon) : !SaveXml.IsNullReference(li)));
            if (queries.WheelRecords)
            {
                Positive(facts, prefix + "wheel/unlocked", slots.Elements("li").Count(li => li.Element("unlocked")?.Value == "True"));
            }
        }

        if (queries.WheelReserveLabel is { } reserveLabel && pawn.Element(reserveLabel) is { } reserve)
        {
            Positive(facts, prefix + "wheel/reserve", reserve.Descendants("li").Count(SaveXml.IsThing));
        }

        if (queries.WheelUnlockedLabel is { } unlockedLabel && pawn.Element(unlockedLabel) is { } unlocked)
        {
            Positive(facts, prefix + "wheel/unlocked", unlocked.Elements("li").Count(li => string.Equals(li.Value.Trim(), "True", StringComparison.Ordinal)));
        }

        if (pawn.Element("roping") is { } roping)
        {
            if (roping.Element("ropedTo") is { } ropedTo && !SaveXml.IsNullReference(ropedTo) && ropedTo.Value.Trim().Length > 0)
            {
                facts[prefix + "roping/ropedTo"] = ropedTo.Value.Trim();
            }

            Positive(facts, prefix + "roping/ropees", roping.Element("ropees")?.Elements("li").Count() ?? 0);
        }

        if (xenotype is not null && queries.ModDefs.Contains(xenotype))
        {
            facts[prefix + "xenotype"] = xenotype;
        }

        foreach (string gene in modGenes.Distinct(StringComparer.Ordinal))
        {
            facts[prefix + "gene/" + gene] = "1";
        }

        foreach (string ability in pawn.Element("abilities")?.Element("abilities")?.Elements("li").Select(SaveXml.Def).OfType<string>()
            .Where(queries.ModDefs.Contains) ?? [])
        {
            facts[prefix + "ability/" + ability] = "1";
        }

        foreach (string trait in pawn.Element("story")?.Element("traits")?.Element("allTraits")?.Elements("li").Select(SaveXml.Def).OfType<string>()
            .Where(queries.ModDefs.Contains) ?? [])
        {
            facts[prefix + "trait/" + trait] = "1";
        }
    }

    // 计数为 0 与不写出等价（空集合与 null 读取后相同）：只记正数。
    private static void Positive(SortedDictionary<string, string> facts, string key, int count)
    {
        if (count > 0)
        {
            facts[key] = count.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static void ReadComponents(XElement? components, string? suffix, SortedDictionary<string, string> facts, bool nested)
    {
        foreach (XElement component in components?.Elements("li") ?? [])
        {
            if (SaveXml.ClassOf(component) is not { } className || !SaveXml.IsModClass(className))
            {
                continue;
            }

            string prefix = "component/" + className + suffix + "/";
            facts[prefix + "present"] = "1";
            if (nested)
            {
                ReadComponentFields(component, prefix, facts);
                continue;
            }
            foreach (XElement child in component.Elements())
            {
                string label = child.Name.LocalName;
                if (SaveXml.IsLeaf(child))
                {
                    if (!SaveXml.HasNullAttribute(child) && child.Value.Length > 0)
                    {
                        facts[prefix + label] = SaveXml.Normalize(child.Value);
                    }
                }
                else if (child.Elements("li").Any() || child.Element("innerList") is not null || child.Element("keys") is not null)
                {
                    int count = child.Element("innerList")?.Elements("li").Count()
                        ?? child.Element("keys")?.Elements("li").Count()
                        ?? child.Elements("li").Count();
                    Positive(facts, prefix + label + "/count", count);
                }
                else
                {
                    Positive(facts, prefix + label + "/nodes", child.Elements().Count());
                }
            }
        }
    }

    private static void ReadComponentFields(XElement owner, string prefix, SortedDictionary<string, string> facts)
    {
        Dictionary<XName, int> positions = [];
        foreach (XElement child in owner.Elements())
        {
            int index = positions.GetValueOrDefault(child.Name) + 1;
            positions[child.Name] = index;
            string path = prefix + child.Name.LocalName + (index > 1 ? "#" + index.ToString(CultureInfo.InvariantCulture) : string.Empty);
            if (SaveXml.IsLeaf(child))
            {
                if (!SaveXml.HasNullAttribute(child) && child.Value.Length > 0)
                {
                    facts[path] = child.Value;
                }
            }
            else if (child.Elements("li").Any() || child.Element("innerList") is not null || child.Element("keys") is not null)
            {
                int count = child.Element("innerList")?.Elements("li").Count() ?? child.Element("keys")?.Elements("li").Count() ?? child.Elements("li").Count();
                Positive(facts, path + "/count", count);
                ReadComponentFields(child, path + "/", facts);
            }
            else
            {
                ReadComponentFields(child, path + "/", facts);
            }
        }
    }

    /// <summary>束缚服装的汇总：数量、上锁与未上锁、锁次数不是默认值的、计时不是默认值的。</summary>
    private sealed class RestraintTally
    {
        private int count;
        private int locked;
        private int unlocked;
        private int customLockCount;
        private int runningTimers;

        public void Add(string? isLocked, string? lockCount, string? defaultLockCount, string? timer, string? defaultTimer)
        {
            count++;
            if (string.Equals(isLocked, "False", StringComparison.Ordinal))
            {
                unlocked++;
            }
            else if (isLocked is not null)
            {
                locked++;
            }

            if (lockCount is not null && defaultLockCount is not null && lockCount != defaultLockCount)
            {
                customLockCount++;
            }

            if (timer is not null && defaultTimer is not null && timer != defaultTimer)
            {
                runningTimers++;
            }
        }

        public void WriteTo(SortedDictionary<string, string> facts)
        {
            facts["restraints/count"] = count.ToString(CultureInfo.InvariantCulture);
            facts["restraints/locked"] = locked.ToString(CultureInfo.InvariantCulture);
            facts["restraints/unlocked"] = unlocked.ToString(CultureInfo.InvariantCulture);
            facts["restraints/customLockCount"] = customLockCount.ToString(CultureInfo.InvariantCulture);
            facts["restraints/runningTimers"] = runningTimers.ToString(CultureInfo.InvariantCulture);
        }
    }
}
