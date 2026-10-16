using System.Xml;
using Verse;

namespace MugirlRace.Validation.Content;

/// <summary>
/// 完整配置（应用补丁后）定义的每个 Def，带运行时类型及其是否也存在于最小配置，另加 RimWorld 由它们生成的 Def（<c>Corpse_</c>、
/// <c>Meat_</c>、<c>Blueprint_</c>、<c>Blueprint_Install_</c>、<c>Frame_</c>、<c>Make_</c>）。查找遵循继承：子类 Def 满足对其基类
/// Def 类型的查找，同 <c>DefDatabase&lt;T&gt;</c>。
/// </summary>
internal sealed class DefIndex
{
    private readonly Dictionary<string, List<DefEntry>> byName = new(StringComparer.Ordinal);

    private DefIndex()
    {
    }

    public int Count => byName.Values.Sum(list => list.Count);

    public IEnumerable<DefEntry> All => byName.Values.SelectMany(list => list);

    public static DefIndex Build(XmlCorpus corpus, ModCatalog catalog, TypeResolver types, XmlInheritanceResolver inheritance)
    {
        var index = new DefIndex();
        foreach (XmlElement node in corpus.Defs)
        {
            if (node.GetAttribute("Abstract").Equals("true", StringComparison.OrdinalIgnoreCase) || !MayRequireSatisfied(node, catalog.Full))
            {
                continue;
            }

            string? defName = node["defName"]?.InnerText.Trim();
            NodeSource? source = corpus.SourceOf(node);
            if (string.IsNullOrEmpty(defName) || source is null)
            {
                continue;
            }

            string typeName = node.GetAttribute("Class") is { Length: > 0 } cls ? cls : node.Name;
            Type? type = types.GetTypeInAnyAssembly(typeName);
            if (type is null || !typeof(Def).IsAssignableFrom(type))
            {
                continue;
            }

            bool minimal = catalog.Minimal.IsActive(source.Pack.PackageId)
                && (source.LoadFolder is null || catalog.FoldersFor(source.Pack, catalog.Minimal).Contains(source.LoadFolder, StringComparer.OrdinalIgnoreCase))
                && MayRequireSatisfied(node, catalog.Minimal);
            index.Add(new DefEntry(defName, type, source.Pack, source.File, minimal, Implied: false));
            foreach ((string impliedName, Type impliedType) in Implied(defName, type, inheritance.Resolve(node), catalog.Full.IsActive("ludeon.rimworld.anomaly")))
            {
                index.Add(new DefEntry(impliedName, impliedType, source.Pack, source.File, minimal, Implied: true));
            }
        }

        return index;
    }

    /// <summary>名为 <paramref name="defName"/>、类型为 <paramref name="wanted"/> 或其子类的 Def。</summary>
    public IReadOnlyList<DefEntry> Find(Type wanted, string defName)
        => byName.TryGetValue(defName, out List<DefEntry>? list) ? list.Where(entry => wanted.IsAssignableFrom(entry.Type)).ToList() : [];

    /// <summary>名为 <paramref name="defName"/> 的任意类型 Def。</summary>
    public IReadOnlyList<DefEntry> FindAny(string defName) => byName.TryGetValue(defName, out List<DefEntry>? list) ? list : [];

    /// <summary>Def 或列表节点上的 <c>MayRequire</c>（全部满足）与 <c>MayRequireAnyOf</c>。</summary>
    public static bool MayRequireSatisfied(XmlElement node, ActiveMods active)
    {
        string all = node.GetAttribute("MayRequire");
        string any = node.GetAttribute("MayRequireAnyOf");
        return (all.Length == 0 || all.Split(',').All(active.IsActive)) && (any.Length == 0 || any.Split(',').Any(active.IsActive));
    }

    private void Add(DefEntry entry)
    {
        if (!byName.TryGetValue(entry.DefName, out List<DefEntry>? list))
        {
            byName[entry.DefName] = list = [];
        }

        list.Add(entry);
    }

    // ThingDefGenerator_Corpses/_Meat/_Buildings 与 RecipeDefGenerator，基于继承解析后的节点。
    private static IEnumerable<(string Name, Type Type)> Implied(string defName, Type type, XmlElement resolved, bool anomalyActive)
    {
        bool isThing = typeof(ThingDef).IsAssignableFrom(type);
        bool isTerrain = typeof(TerrainDef).IsAssignableFrom(type);
        if (isThing && resolved["race"] is { } race && Text(resolved, "category") == "Pawn" && Text(race, "hasCorpse") != "false")
        {
            // 4979 ThingDefGenerator_Corpses：复用别的种族尸体时不生成自己的普通或异象尸体。
            string? linkedCorpse = Text(race, "linkedCorpseKind");
            if (string.IsNullOrEmpty(linkedCorpse) || linkedCorpse == defName)
            {
                yield return ("Corpse_" + defName, typeof(ThingDef));
                if (anomalyActive && string.Equals(Text(race, "hasUnnaturalCorpse"), "true", StringComparison.OrdinalIgnoreCase))
                {
                    yield return ("UnnaturalCorpse_" + defName, typeof(ThingDef));
                }
            }

            if (Text(race, "hasMeat") != "false" && race["useMeatFrom"] is null && race["specificMeatDef"] is null && Text(race, "fleshType") != "Mechanoid")
            {
                yield return ("Meat_" + defName, typeof(ThingDef));
            }
        }

        if ((isThing || isTerrain) && !string.IsNullOrEmpty(Text(resolved, "designationCategory")))
        {
            yield return ("Blueprint_" + defName, typeof(ThingDef));
            yield return ("Frame_" + defName, typeof(ThingDef));
        }

        if (isThing && !string.IsNullOrEmpty(Text(resolved, "minifiedDef")))
        {
            yield return ("Blueprint_Install_" + defName, typeof(ThingDef));
        }

        if (isThing && resolved["recipeMaker"] is not null)
        {
            yield return ("Make_" + defName, typeof(RecipeDef));
        }
    }

    private static string? Text(XmlElement element, string child) => element[child]?.InnerText.Trim();
}
