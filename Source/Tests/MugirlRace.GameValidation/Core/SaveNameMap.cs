using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Mugirl.GameValidation.Core;

/// <summary>升级清单（K1：<c>Mugirl.SaveUpgrade.SaveUpgradeManifest.ToJson()</c>）的只读视图，按清单自己的命名空间查询。</summary>
/// <remarks>
/// <para>
/// Def 按（根类型, 旧名）查询：同一个 defName 可以分属不同根类型且处理不同（<c>Consumed_MugirlMilk</c> 的 HediffDef 与 ThoughtDef 改成不同新名，
/// <c>Mugirl_Slave</c> 作为 ThingCategoryDef 改名、作为 PawnKindDef 保留）。原版隐含 Def（<c>Corpse_</c>、<c>Blueprint_</c>、<c>Make_</c> 等）
/// 由来源行按原版的生成规则派生（清单不单独列出它们）。标签按（owner 选择器, 旧名）求值，值按（上下文, 旧值）登记。
/// </para>
/// <para>只解析 JSON 与原版 XML 形状，不触碰 Verse 状态，-Plumbing 构建与离线测试都能使用。</para>
/// </remarks>
internal sealed partial class SaveNameMap
{
    /// <summary>
    /// 原版由 Def 生成的隐含 Def：来源根类型、隐含 Def 的根类型、名字前缀与后缀（ThingDefGenerator_Buildings、_Corpses、_Meat、
    /// RecipeDefGenerator、Techprint、Neurotrainer、Psytrainer）。
    /// </summary>
    private static readonly (string Source, string Target, string Prefix, string Suffix)[] DerivedRules =
    [
        ("ThingDef", "ThingDef", "Blueprint_", ""),
        ("ThingDef", "ThingDef", "Blueprint_Install_", ""),
        ("ThingDef", "ThingDef", "Frame_", ""),
        ("ThingDef", "ThingDef", "Corpse_", ""),
        ("ThingDef", "ThingDef", "UnnaturalCorpse_", ""),
        ("ThingDef", "ThingDef", "Meat_", ""),
        ("ThingDef", "RecipeDef", "Make_", ""),
        ("ThingDef", "RecipeDef", "Make_", "Bulk"),
        ("ThingDef", "RecipeDef", "Administer_", ""),
        ("TerrainDef", "ThingDef", "Blueprint_", ""),
        ("TerrainDef", "ThingDef", "Frame_", ""),
        ("ResearchProjectDef", "ThingDef", "Techprint_", ""),
        ("SkillDef", "ThingDef", "Neurotrainer_", ""),
        ("AbilityDef", "ThingDef", "Psytrainer_", ""),
    ];

    private readonly Dictionary<string, string?> types = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Root, string Old), string?> defs = [];
    private readonly Dictionary<string, List<(string Root, string? New)>> defsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SaveLabelRow>> labelsByOld = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SaveLabelRow>> defaultsByLabel = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SaveLabelRow>> consumedLabelsByOld = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> enumsByOld = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string?>> valuesByOld = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Context, string Old), string?> values = [];
    private readonly Dictionary<(string Enum, string Old), string> enumValues = [];
    private readonly List<SaveValueSite> valueSites = [];
    private readonly HashSet<string> consumedTypes = new(StringComparer.Ordinal);
    private readonly HashSet<(string Root, string Name)> consumedDefs = [];
    private readonly HashSet<(string Enum, string Value)> consumedEnumMembers = [];
    private readonly HashSet<(string Context, string Value)> consumedValues = [];
    private readonly HashSet<string> endedJobTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> claimedJobTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> lordPolicies = new(StringComparer.Ordinal);
    private readonly HashSet<string> newNames = new(StringComparer.Ordinal);
    private readonly List<SaveUpgradeStepRow> steps = [];

    private SaveNameMap()
    {
    }

    /// <summary>空映射（升级清单不可用时）：所有名字保持不变。</summary>
    public static SaveNameMap Empty { get; } = new();

    /// <summary>清单声明的当前存档架构版本；空映射为 -1。</summary>
    public int Schema { get; private set; } = -1;

    /// <summary>版本戳标签（K1 stampLabel）。</summary>
    public string? StampLabel { get; private set; }

    /// <summary>清单中的步骤。</summary>
    public IReadOnlyList<SaveUpgradeStepRow> Steps => steps;

    /// <summary>设置文件中的设置类旧名与新名（K1 settings.fileClass）；清单没有给出时为 null。</summary>
    public (string Old, string New)? SettingsFileClass { get; private set; }

    /// <summary>设置文件中改名或删除的键（K1 settings.labels）。</summary>
    public IReadOnlyList<(string Old, string? New)> SettingsLabels { get; private set; } = [];

    public int TypeCount => types.Count;

    /// <summary>Def 行数（含派生的隐含 Def）。</summary>
    public int DefCount => defs.Count;

    public int LabelCount => labelsByOld.Values.Sum(rows => rows.Count);

    public static SaveNameMap Parse(string json)
    {
        var map = new SaveNameMap();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("schema", out JsonElement schema) && schema.TryGetInt32(out int number))
        {
            map.Schema = number;
        }

        map.StampLabel = Text(root, "stampLabel");
        foreach (JsonElement row in Rows(root, "types"))
        {
            if (Text(row, "old") is { } old)
            {
                map.types[old] = Text(row, "new");
                map.AddNew(Text(row, "new"));
            }
        }

        var explicitDefs = new List<(string Root, string Old, string? New)>();
        foreach (JsonElement row in Rows(root, "defs"))
        {
            if (Text(row, "root") is { } defRoot && Text(row, "old") is { } old)
            {
                explicitDefs.Add((defRoot, old, Text(row, "new")));
            }
        }

        foreach ((string defRoot, string old, string? now) in explicitDefs)
        {
            map.AddDef(defRoot, old, now, overwrite: true);
        }

        // 隐含 Def 由来源行派生；清单显式列出的同名行优先。
        foreach ((string defRoot, string old, string? now) in explicitDefs)
        {
            foreach ((string source, string target, string prefix, string suffix) in DerivedRules.Where(rule => rule.Source == defRoot))
            {
                map.AddDef(target, prefix + old + suffix, now is null ? null : prefix + now + suffix, overwrite: false);
            }
        }

        foreach (JsonElement row in Rows(root, "labels"))
        {
            if (Text(row, "old") is not { } old)
            {
                continue;
            }

            var label = new SaveLabelRow(SaveOwnerSelector.Parse(Text(row, "owner")), old, Text(row, "new"), Text(row, "disposition") ?? "rename", Text(row, "step"));
            Add(map.labelsByOld, old, label);
            map.AddNew(label.New);
        }

        foreach (JsonElement row in Rows(root, "enums"))
        {
            if (Text(row, "enum") is { } enumType && Text(row, "old") is { } old && Text(row, "new") is { } now)
            {
                Add(map.enumsByOld, old, now);
                map.enumValues[(enumType, old)] = now;
                map.AddNew(now);
            }
        }

        foreach (JsonElement row in Rows(root, "values"))
        {
            if (Text(row, "context") is { } valueContext && Text(row, "old") is { } old)
            {
                Add(map.valuesByOld, old, Text(row, "new"));
                map.values[(valueContext, old)] = Text(row, "new");
                map.AddNew(Text(row, "new"));
            }
        }

        foreach (JsonElement row in Rows(root, "valueContexts"))
        {
            if (Text(row, "name") is { } name && Text(row, "label") is { } label)
            {
                map.valueSites.Add(new SaveValueSite(name, SaveOwnerSelector.Parse(Text(row, "owner")), label,
                    Text(row, "kind") ?? "text", Text(row, "defRoot"), Text(row, "part") ?? "items", false));
            }
        }

        foreach (JsonElement row in Rows(root, "enumSites"))
        {
            if (Text(row, "enum") is { } name && Text(row, "label") is { } label)
            {
                map.valueSites.Add(new SaveValueSite(name, SaveOwnerSelector.Parse(Text(row, "owner")), label,
                    "enum", null, Text(row, "part") ?? "items", row.TryGetProperty("flags", out JsonElement flags) && flags.ValueKind == JsonValueKind.True));
            }
        }

        foreach (JsonElement row in Rows(root, "defaults"))
        {
            if (Text(row, "label") is { } label)
            {
                Add(map.defaultsByLabel, label, new SaveLabelRow(SaveOwnerSelector.Parse(Text(row, "owner")), label, null, "default", null, Text(row, "oldDefault")));
            }
        }

        foreach (JsonElement row in Rows(root, "steps"))
        {
            if (Text(row, "id") is not { } id)
            {
                continue;
            }

            List<string> consumes = Strings(row, "consumes");
            map.steps.Add(new SaveUpgradeStepRow(id, Text(row, "phase") ?? "?", Text(row, "module") ?? "?", consumes));
            foreach (string consumed in consumes)
            {
                map.AddConsumption(id, consumed);
            }
        }

        foreach (JsonElement row in Rows(root, "jobPolicies"))
        {
            if (Text(row, "type") is not { } type || Text(row, "policy") is not { } policy)
            {
                continue;
            }

            if (policy.StartsWith("claimed:", StringComparison.Ordinal))
            {
                map.claimedJobTypes[type] = policy["claimed:".Length..];
            }
            else if (policy == "endInFlight")
            {
                map.endedJobTypes.Add(type);
            }
        }

        foreach (JsonElement row in Rows(root, "lordPolicies"))
        {
            if (Text(row, "type") is { } type && Text(row, "policy") is { } policy)
            {
                map.lordPolicies[type] = policy;
            }
        }

        if (root.TryGetProperty("settings", out JsonElement settings) && settings.ValueKind == JsonValueKind.Object)
        {
            if (settings.TryGetProperty("fileClass", out JsonElement fileClass) && fileClass.ValueKind == JsonValueKind.Object
                && Text(fileClass, "old") is { } oldClass && Text(fileClass, "new") is { } newClass)
            {
                map.SettingsFileClass = (oldClass, newClass);
            }

            map.SettingsLabels = [.. Rows(settings, "labels").Where(r => Text(r, "old") is not null).Select(r => (Text(r, "old")!, Text(r, "new")))];
        }

        return map;
    }

    /// <summary>旧类型全名的新名；drop 为 null；不在清单中时原样返回。</summary>
    public string? Type(string name) => types.TryGetValue(name, out string? now) ? now : name;

    public bool IsDroppedType(string name) => types.TryGetValue(name, out string? now) && now is null;

    /// <summary>根类型 <paramref name="root"/> 中旧 defName 的新名（含隐含 Def）；drop 为 null；不在清单中时原样返回。</summary>
    public string? Def(string root, string name) => defs.TryGetValue((root, name), out string? now) ? now : name;

    /// <summary>只有该 Def 根类型明确登记的改名或删除才是残留；其他根中的合法同名 Def 不受影响。</summary>
    public bool IsLegacyDef(string root, string name) => defs.TryGetValue((root, name), out string? now) && now != name;

    /// <summary>
    /// 根类型未知时旧 defName 的新名：清单中该名字的所有行结论一致时给出它，否则（<paramref name="ambiguous"/>）原样返回。
    /// </summary>
    public string? DefAnyRoot(string name, out bool ambiguous)
    {
        ambiguous = false;
        if (!defsByName.TryGetValue(name, out List<(string Root, string? New)>? rows))
        {
            return name;
        }

        string? first = rows[0].New;
        if (rows.All(r => r.New == first))
        {
            return first;
        }

        ambiguous = true;
        return name;
    }

    /// <summary>旧 defName 的所有已登记根类型。</summary>
    public IEnumerable<string> DefRootsOf(string name) => defsByName.TryGetValue(name, out List<(string Root, string? New)>? rows) ? rows.Select(r => r.Root) : [];

    public bool IsDroppedDef(string root, string name) => defs.TryGetValue((root, name), out string? now) && now is null;

    /// <summary>
    /// JobDriver 类型的进行中工作是否由清单明确要求结束：旧档只认可 endInFlight；preserve、claimed 和未登记类型都保留。
    /// <paramref name="driverClass"/> 可以是旧全名，也可以是经类型映射后的新名。
    /// </summary>
    public bool EndsJob(string driverClass, bool schemaZero)
        => schemaZero && !IsListed(claimedJobTypes.Keys, driverClass) && IsListed(endedJobTypes, driverClass);

    /// <summary>LordJob 类型（旧全名或新名）的处理（resetGraph、removeLord、claimed:&lt;步骤&gt;）；没有登记时为 null。</summary>
    public string? LordPolicy(string lordJobClass)
        => lordPolicies.FirstOrDefault(p => p.Key == lordJobClass || Type(p.Key) == lordJobClass).Value;

    /// <summary><paramref name="owner"/> 中标签 <paramref name="label"/> 的处理：只看 owner 选择器选中 <paramref name="owner"/> 的标签行。</summary>
    public SaveLabelFate LabelFate(XElement owner, string label, ISaveOwnerContext context)
    {
        IEnumerable<SaveLabelRow> rows = (labelsByOld.TryGetValue(label, out List<SaveLabelRow>? found) ? found : [])
            .Concat(consumedLabelsByOld.TryGetValue(label, out List<SaveLabelRow>? consumed) ? consumed : []);
        return Decide([.. rows.Where(row => row.Owner.Matches(owner, context))]);
    }

    /// <summary>
    /// 没有元素可求值时（事实的键、查询名）标签 <paramref name="label"/> 的处理：只看 owner 选择器满足 <paramref name="owner"/> 的标签行。
    /// </summary>
    public SaveLabelFate LabelFate(string label, Func<SaveOwnerSelector, bool> owner)
    {
        IEnumerable<SaveLabelRow> rows = (labelsByOld.TryGetValue(label, out List<SaveLabelRow>? found) ? found : [])
            .Concat(consumedLabelsByOld.TryGetValue(label, out List<SaveLabelRow>? consumed) ? consumed : []);
        return Decide([.. rows.Where(row => owner(row.Owner))]);
    }

    /// <summary>（owner, 标签）是否登记了默认值改变（K1 defaults）：旧值等于新默认值时新构建不再写出它。</summary>
    public bool HasChangedDefault(XElement owner, string label, ISaveOwnerContext context)
        => defaultsByLabel.TryGetValue(label, out List<SaveLabelRow>? rows) && rows.Any(row => row.Owner.Matches(owner, context));

    /// <summary>全部标签行（改名、删除、消费），供按 owner 限定的残留旧名扫描使用。</summary>
    public IEnumerable<SaveLabelRow> LabelRows => labelsByOld.Values.SelectMany(rows => rows).Concat(consumedLabelsByOld.Values.SelectMany(rows => rows));

    /// <summary>步骤是否声明消费了类型 <paramref name="name"/>（旧全名或新名）。</summary>
    public bool ConsumesType(string name) => IsListed(consumedTypes, name);

    /// <summary>步骤是否声明消费了 Def（旧名或新名；根类型未知时 <paramref name="root"/> 为 null，任一根类型即可）。</summary>
    public bool ConsumesDef(string? root, string name)
        => consumedDefs.Any(d => (root is null || d.Root == root) && (d.Name == name || Def(d.Root, d.Name) == name));

    /// <summary>步骤是否声明消费了保存的值或枚举成员 <paramref name="value"/>。</summary>
    public bool ConsumesValue(string value, XElement leaf, ISaveOwnerContext context)
        => valueSites.Any(site => site.Matches(leaf, context, this) && (site.Kind == "enum"
            ? consumedEnumMembers.Contains((site.Name, value)) : consumedValues.Contains((site.Name, value))));

    /// <summary>只有已登记的位置才翻译保存字符串、枚举、字符串 Def 与类型；未知位置保持原值。</summary>
    public string? TranslateSavedValue(string value, XElement leaf, ISaveOwnerContext context)
    {
        List<string?> translated = [.. valueSites.Where(site => site.Matches(leaf, context, this)).Select(site => TranslateAtSite(value, site))];
        return translated.Count > 0 && translated.All(v => v == translated[0]) ? translated[0] : value;
    }

    public bool IsLegacyValue(XElement leaf, ISaveOwnerContext context)
        => TranslateSavedValue(leaf.Value, leaf, context) != leaf.Value || ConsumesValue(leaf.Value, leaf, context);

    private string? TranslateAtSite(string value, SaveValueSite site)
        => site.Kind switch
        {
            "enum" when site.Flags => string.Join(", ", value.Split(',').Select(v => enumValues.GetValueOrDefault((site.Name, v.Trim()), v.Trim()))),
            "enum" => enumValues.GetValueOrDefault((site.Name, value), value),
            "type" => Type(value),
            "def" or "defReference" when site.DefRoot is { } root => Def(root, value),
            "text" => values.TryGetValue((site.Name, value), out string? mapped) ? mapped : value,
            _ => value,
        };

    /// <summary>清单是否把值 <paramref name="before"/> 映射为 <paramref name="after"/>（类型、任一根类型的 Def、枚举成员、保存的值、ThingID）。</summary>
    public bool ExplainsValueChange(string before, string after, XElement leaf, ISaveOwnerContext context)
        => (before != after && TranslateSavedValue(before, leaf, context) == after)
            || (SaveDefRoots.RootOf(leaf) is { } root && Def(root, before) == after)
            || (leaf.Attribute("Class")?.Value == before && Type(before) == after)
            || (before != after && leaf.Name.LocalName == "id" && leaf.Parent is { } thing && SaveXml.IsThing(thing) && TranslateId(before) == after);

    /// <summary>改名或删除、且没有被任何新名重用的旧类型全名。</summary>
    public HashSet<string> LegacyOnlyTypes()
        => [.. types.Where(p => p.Value != p.Key && !newNames.Contains(p.Key)).Select(p => p.Key)];

    /// <summary>
    /// 只属于旧构建的 defName：清单把它在每个登记的根类型中都改名或删除，没有被新名重用，也不是当前构建的 Def 名
    /// （<paramref name="currentDefNames"/>：任一根类型）。
    /// </summary>
    public HashSet<string> LegacyOnlyDefs(IReadOnlySet<string> currentDefNames)
        => [.. defsByName.Where(p => p.Value.All(r => r.New != p.Key) && !newNames.Contains(p.Key) && !currentDefNames.Contains(p.Key)).Select(p => p.Key)];

    /// <summary>改名的枚举成员与保存的值中不再使用的旧值（只应在 Mod 拥有的位置检查）。</summary>
    public HashSet<string> LegacyOnlyValues()
        => [.. enumsByOld.Where(p => !p.Value.Contains(p.Key)).Select(p => p.Key)
            .Concat(valuesByOld.Where(p => !p.Value.Contains(p.Key)).Select(p => p.Key))
            .Where(name => !newNames.Contains(name))];

    /// <summary>ThingID 与 Thing 引用（<c>[Thing_]&lt;defName&gt;&lt;数字&gt;</c>）按 ThingDef 映射改写；不匹配或被删除时原样返回。</summary>
    public string TranslateId(string token)
    {
        Match match = ThingIdPattern().Match(token);
        if (!match.Success)
        {
            return token;
        }

        // defName 本身可以以数字结尾：从最长的 defName 开始试，取清单中存在的那个。
        string prefix = match.Groups["prefix"].Value;
        string body = match.Groups["body"].Value;
        int digits = body.Length - body.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Length;
        for (int split = body.Length - 1; split >= body.Length - digits; split--)
        {
            if (defs.TryGetValue(("ThingDef", body[..split]), out string? now))
            {
                return now is null ? token : prefix + now + body[split..];
            }
        }

        return token;
    }

    /// <summary>
    /// 清单诊断用的部分改名预览：owner 标签、Class、明确 Def/值位置与 Thing.id。
    /// 消费标签保留为待执行的证据，引用只由实际读取轨迹改写；此方法不是规范升级结果，不能用于证明完整保存。
    /// </summary>
    /// <remarks>标签的 owner 选择器在翻译前的原始文档上求值（旧类型名经类型映射，旧 defName 按旧构建的组件关系）。</remarks>
    public SaveApplyReport Apply(XDocument document, ISaveOwnerContext context)
    {
        var report = new SaveApplyReport();
        if (SaveXml.Game(document) is not { } game)
        {
            return report;
        }

        var labelEdits = new List<(XElement Element, SaveLabelFate Fate)>();
        foreach (XElement element in game.Descendants())
        {
            if (element.Parent is { } owner && (labelsByOld.ContainsKey(element.Name.LocalName) || consumedLabelsByOld.ContainsKey(element.Name.LocalName)))
            {
                SaveLabelFate fate = LabelFate(owner, element.Name.LocalName, context);
                if (fate.Kind != SaveLabelFateKind.Unchanged)
                {
                    labelEdits.Add((element, fate));
                }
            }
        }

        // 在改写 Class 和标签之前按旧位置求值，避免同一文本在别的 owner 或字典另一侧被误映射。
        foreach (XElement leaf in game.Descendants().Where(e => !e.HasElements))
        {
            if (TranslateSavedValue(leaf.Value, leaf, context) is { } translated && translated != leaf.Value)
            {
                leaf.Value = translated;
            }
        }

        foreach (XElement element in game.DescendantsAndSelf())
        {
            if (element.Attribute("Class") is { } classAttribute && Type(classAttribute.Value) is { } newType && newType != classAttribute.Value)
            {
                classAttribute.Value = newType;
                report.Types++;
            }

            if (element.HasElements || element.Value.Length == 0)
            {
                continue;
            }

            string text = element.Value.Trim();
            if (defsByName.ContainsKey(text))
            {
                // 认得出根类型时按（根类型, 旧名）翻译；认不出时只用各根类型一致的映射。被删除的 Def 保持原名（由比较方解释）。
                string? root = SaveDefRoots.RootOf(element);
                string? translated;
                if (root is not null)
                {
                    translated = Def(root, text);
                }
                else
                {
                    // 任意保存字符串不是 Def 引用；显式字符串 Def 位置已由 valueContexts 在前面处理。
                    translated = text;
                }

                if (translated is not null && translated != text)
                {
                    element.Value = translated;
                    report.Defs++;
                }

                continue;
            }

            // 实际引用的别名只由真实 Scribe read trace 证明；普通文字即使长得像 Thing_ID 也不能改。
            string id = element.Name.LocalName == "id" && element.Parent is { } thing && SaveXml.IsThing(thing) ? TranslateId(text) : text;
            if (id != text)
            {
                element.Value = id;
                report.Ids++;
            }
        }

        foreach ((XElement element, SaveLabelFate fate) in labelEdits)
        {
            switch (fate.Kind)
            {
                case SaveLabelFateKind.Renamed:
                    element.Name = fate.NewLabel!;
                    report.LabelsRenamed++;
                    break;
                case SaveLabelFateKind.Dropped:
                    report.LabelsRemoved.Add($"{Path(element)} ({fate})");
                    element.Remove();
                    break;
                case SaveLabelFateKind.Consumed:
                    report.PendingConsumptions.Add($"{Path(element)} ({fate})");
                    break;
                case SaveLabelFateKind.Ambiguous:
                    report.AmbiguousLabels.Add($"{Path(element)} ({fate})");
                    break;
                case SaveLabelFateKind.Unchanged:
                default:
                    break;
            }
        }

        return report;
    }

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture,
            $"schema {Schema}, {types.Count} type(s), {defs.Count} def(s) incl. implied, {LabelCount} label(s), {enumsByOld.Count} enum value(s), {valuesByOld.Count} saved value(s), "
            + $"{steps.Count} step(s), {endedJobTypes.Count + claimedJobTypes.Count} job polic(ies), {lordPolicies.Count} lord polic(ies)");

    /// <summary>元素在存档中的路径（报告用）。</summary>
    public static string Path(XElement element)
        => string.Join("/", element.AncestorsAndSelf().Reverse().Skip(1).Select(e => e.Name.LocalName));

    private static SaveLabelFate Decide(List<SaveLabelRow> rows)
    {
        if (rows.Count == 0)
        {
            return SaveLabelFate.Unchanged;
        }

        SaveLabelFate first = rows[0].Fate;
        return rows.All(r => r.Fate.Kind == first.Kind && r.Fate.NewLabel == first.NewLabel
            && (first.Kind != SaveLabelFateKind.Consumed || r.Fate.Step == first.Step))
            ? first
            : new SaveLabelFate(SaveLabelFateKind.Ambiguous, null, null, string.Join(" | ", rows.Select(r => r.Owner.Text + ": " + r.Fate.Kind)));
    }

    // 清单按旧全名登记；比较方的文档可能已经翻译成新名。
    private bool IsListed(IEnumerable<string> legacyTypes, string name)
        => legacyTypes.Any(type => type == name || Type(type) == name);

    private void AddDef(string root, string old, string? now, bool overwrite)
    {
        if (!overwrite && defs.ContainsKey((root, old)))
        {
            return;
        }

        defs[(root, old)] = now;
        if (!defsByName.TryGetValue(old, out List<(string Root, string? New)>? rows))
        {
            rows = [];
            defsByName[old] = rows;
        }

        rows.RemoveAll(r => r.Root == root);
        rows.Add((root, now));
        AddNew(now);
    }

    // type:<名>、def:<根类型>/<名>、label:<owner>/<标签>、enum:<枚举>.<成员>、value:<上下文>/<值>。
    private void AddConsumption(string step, string consumed)
    {
        int colon = consumed.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return;
        }

        string kind = consumed[..colon];
        string body = consumed[(colon + 1)..];
        switch (kind)
        {
            case "type":
                consumedTypes.Add(body);
                break;
            case "def" when body.IndexOf('/', StringComparison.Ordinal) is > 0 and var slash:
                consumedDefs.Add((body[..slash], body[(slash + 1)..]));
                break;
            case "label" when body.LastIndexOf('/') is > 0 and var last:
                string label = body[(last + 1)..];
                Add(consumedLabelsByOld, label, new SaveLabelRow(SaveOwnerSelector.Parse(body[..last]), label, null, "consumed", step));
                break;
            case "enum" when body.LastIndexOf('.') is > 0 and var dot:
                consumedEnumMembers.Add((body[..dot], body[(dot + 1)..]));
                break;
            case "value" when body.IndexOf('/', StringComparison.Ordinal) is > 0 and var separator:
                consumedValues.Add((body[..separator], body[(separator + 1)..]));
                break;
            default:
                break;
        }
    }

    private void AddNew(string? name)
    {
        if (name is not null)
        {
            newNames.Add(name);
        }
    }

    private static void Add<T>(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out List<T>? list))
        {
            list = [];
            map[key] = list;
        }

        list.Add(value);
    }

    private static void Add<T>(Dictionary<string, HashSet<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out HashSet<T>? set))
        {
            set = [];
            map[key] = set;
        }

        set.Add(value);
    }

    private static IEnumerable<JsonElement> Rows(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object)]
            : [];

    private static string? Text(JsonElement row, string name)
        => row.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static List<string> Strings(JsonElement row, string name)
        => row.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)]
            : [];

    [GeneratedRegex(@"^(?<prefix>Thing_)?(?<body>[A-Za-z_][A-Za-z0-9_]*?\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ThingIdPattern();
}
