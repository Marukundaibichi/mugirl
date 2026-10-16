using System.Xml;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Legacy;
using MugirlRace.Validation.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MugirlRace.Validation.SaveUpgrade;

/// <summary>
/// 被测构建在存档意义上的全部名字：类型与存档根、按 owner 的标签（与语料同一个分析）、Def（按数据库根）、设置类、
/// 存进字符串字段的字面量、Keyed 键，以及 <c>Mugirl.SaveUpgrade</c> 之外出现的字符串字面量（旧名残留检查）。
/// </summary>
internal sealed class NewBuild
{
    /// <summary>升级层的命名空间：只有这里可以提到旧名。</summary>
    public const string UpgradeNamespace = "Mugirl.SaveUpgrade";

    private readonly Dictionary<string, List<LabelFact>> labelsByOwner;
    private readonly Dictionary<string, HashSet<string>> defsByRoot = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> defFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SortedSet<string>> toolIds = new(StringComparer.Ordinal);

    public NewBuild(ModuleDefinition module, AssemblySchema schema, ContentModel content, string modRoot)
    {
        Module = module;
        Schema = schema;
        Types = CecilNames.AllTypes(module).Where(t => !CecilNames.IsCompilerGenerated(t)).ToDictionary(CecilNames.Type, t => t, StringComparer.Ordinal);
        labelsByOwner = schema.Scribe.Labels.GroupBy(fact => fact.Owner, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        // 存档按名字找 Def，不看哪个包定义它：旧内容覆盖过的原版 Def（HumanlikeConstant）仍由游戏提供，属于 Unchanged。
        foreach (DefEntry entry in content.Defs.All)
        {
            string root = DefRoots.Database(entry.Type).Name;
            if (!defsByRoot.TryGetValue(root, out HashSet<string>? names))
            {
                defsByRoot[root] = names = new HashSet<string>(StringComparer.Ordinal);
            }

            names.Add(entry.DefName);
            if (ContentModel.IsOurs(entry.Pack))
            {
                defFiles.TryAdd(entry.DefName, XmlPaths.File(modRoot, entry.File));
            }
        }

        foreach (XmlElement node in content.Corpus.Defs.Where(n => ContentModel.IsOurs(content.Corpus.SourceOf(n)?.Pack)))
        {
            if (node["defName"]?.InnerText.Trim() is { Length: > 0 } defName && !node.GetAttribute("Abstract").Equals("true", StringComparison.OrdinalIgnoreCase)
                && content.Inheritance.Resolve(node)["tools"] is { } tools)
            {
                if (!toolIds.TryGetValue(defName, out SortedSet<string>? ids))
                {
                    toolIds[defName] = ids = new SortedSet<string>(StringComparer.Ordinal);
                }

                ids.UnionWith(ContentSchema.ToolIds(tools));
            }
        }

        PatchesBackCompatibleType = CecilNames.AllTypes(module).SelectMany(t => t.CustomAttributes.Concat(t.Methods.SelectMany(m => m.CustomAttributes)))
            .Any(attribute => attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch"
                && attribute.ConstructorArguments.Any(argument => argument.Value is TypeReference { FullName: "Verse.BackCompatibility" })
                && attribute.ConstructorArguments.Any(argument => argument.Value is "GetBackCompatibleType"));

        SettingsClass = Types.Values.Where(t => !t.IsAbstract && CecilNames.BaseTypeNames(t).Contains("Verse.ModSettings", StringComparer.Ordinal))
            .Select(CecilNames.Type).Order(StringComparer.Ordinal).FirstOrDefault();
        ModClassShortNames = Types.Values.Where(t => CecilNames.BaseTypeNames(t).Contains("Verse.Mod", StringComparer.Ordinal)).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        SavedValues = schema.SavedValues.Select(row => (row[0], row[1])).ToHashSet();
        foreach (string language in KeyedIndex.Languages.Keys)
        {
            KeyedKeys.UnionWith(content.Keyed.KeysOf(language, content.Catalog.UnderTest).Keys);
        }

        foreach (MethodDefinition method in Types.Values.Concat(CecilNames.AllTypes(module).Where(CecilNames.IsCompilerGenerated)).SelectMany(t => t.Methods).Where(m => m.HasBody))
        {
            if (CecilNames.NamespaceOf(method.DeclaringType).StartsWith(UpgradeNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code == Code.Ldstr && instruction.Operand is string literal && !LiteralSites.ContainsKey(literal))
                {
                    LiteralSites[literal] = CecilNames.Type(CecilNames.UserType(method.DeclaringType)) + "::" + CecilNames.UserMethodName(method);
                }
            }
        }
    }

    public ModuleDefinition Module { get; }

    public AssemblySchema Schema { get; }

    public IReadOnlyDictionary<string, TypeDefinition> Types { get; }

    /// <summary>非抽象的 ModSettings 子类（完整名）；没有时为空。</summary>
    public string? SettingsClass { get; }

    /// <summary>Verse.Mod 子类的短名（设置文件名的一部分）。</summary>
    public IReadOnlySet<string> ModClassShortNames { get; }

    /// <summary>（字段 Owner::name，JSON 引号字面量）。</summary>
    public IReadOnlySet<(string Field, string Value)> SavedValues { get; }

    public HashSet<string> KeyedKeys { get; } = new(StringComparer.Ordinal);

    /// <summary><c>Mugirl.SaveUpgrade</c> 之外的字符串字面量 → 第一个使用它的方法。</summary>
    public Dictionary<string, string> LiteralSites { get; } = new(StringComparer.Ordinal);

    public SaveRoots Roots => Schema.Roots;

    /// <summary>构建是否给 <c>Verse.BackCompatibility.GetBackCompatibleType</c> 打补丁（独立文档中的类名的后备解析，决定 D4）。</summary>
    public bool PatchesBackCompatibleType { get; }

    /// <summary>ThingDef（继承解析后）的工具 id。</summary>
    public IReadOnlySet<string> ToolIds(string thingDef) => toolIds.TryGetValue(thingDef, out SortedSet<string>? ids) ? ids : new SortedSet<string>(StringComparer.Ordinal);

    public string? RootOf(string type) => Types.TryGetValue(type, out TypeDefinition? definition) ? Roots.RootOf(definition) : null;

    /// <summary>
    /// owner 读写的标签：自身与本 Mod 基类的；或者 owner 的每个具体子类都读写（标签移到子类时仍算同一个 owner 读写）。
    /// </summary>
    public IReadOnlyList<LabelFact> LabelsOf(string owner, string label)
    {
        var result = new List<LabelFact>();
        if (!Types.TryGetValue(owner, out TypeDefinition? type))
        {
            return result;
        }

        for (TypeDefinition? current = type; current is not null && current.Module == Module; current = CecilNames.TryResolve(current.BaseType))
        {
            if (labelsByOwner.TryGetValue(CecilNames.Type(current), out List<LabelFact>? facts))
            {
                result.AddRange(facts.Where(f => f.Label == label));
            }
        }

        if (result.Count > 0)
        {
            return result;
        }

        List<TypeDefinition> concrete = Types.Values.Where(t => !t.IsAbstract && t != type && Inherits(t, type)).ToList();
        if (concrete.Count > 0 && concrete.All(c => LabelsOf(CecilNames.Type(c), label).Count > 0))
        {
            result.AddRange(concrete.SelectMany(c => LabelsOf(CecilNames.Type(c), label)));
        }

        return result;
    }

    /// <summary>owner 自身（不含基类）读写的全部标签。</summary>
    public IEnumerable<string> OwnLabels(string owner) => labelsByOwner.TryGetValue(owner, out List<LabelFact>? facts) ? facts.Select(f => f.Label).Distinct(StringComparer.Ordinal) : [];

    /// <summary>
    /// 名为 <paramref name="defName"/> 的 Def（任何包定义的）是否存在于数据库 <paramref name="root"/>（以 <c>?</c> 开头的未解析根与 <c>*</c>
    /// 匹配任意数据库）。
    /// </summary>
    public bool HasDef(string root, string defName)
        => root.StartsWith('?') || root == "*"
            ? defsByRoot.Values.Any(names => names.Contains(defName))
            : defsByRoot.TryGetValue(root, out HashSet<string>? names) && names.Contains(defName);

    /// <summary>定义该 Def 的内容文件（相对 Mod 根目录）。</summary>
    public string? DefFile(string defName) => defFiles.GetValueOrDefault(defName);

    private bool Inherits(TypeDefinition type, TypeDefinition ancestor)
    {
        for (TypeDefinition? current = CecilNames.TryResolve(type.BaseType); current is not null && current.Module == Module; current = CecilNames.TryResolve(current.BaseType))
        {
            if (current == ancestor)
            {
                return true;
            }
        }

        return false;
    }
}
