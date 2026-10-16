using System.Reflection;
using System.Xml;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Reporting;
using Verse;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// C. Def 之间按名字的引用（<c>DirectXmlCrossRefLoader</c>）：
/// <list type="bullet">
/// <item><c>C.def-references</c>：Mod 的 Def 与补丁插入的 XML 中每个交叉引用都能解析到正确类型的 Def（否则游戏记录
/// “Could not resolve cross-reference”）；只存在于 DLC 或可选 Mod 的 Def 必须在节点或其祖先上写 <c>MayRequire</c>，或位于只在该内容
/// 启用时加载的条件目录中。<c>costList</c>、<c>statBases</c> 一类由 <c>LoadDataFromXmlCustom</c> 以元素名引用 Def 的写法一并检查。</item>
/// <item><c>C.def-conflicts</c>：Mod 不重复定义同一个 Def，也不定义与原版、DLC 或依赖同库同名的 Def（后加载的会覆盖或报错）。</item>
/// </list>
/// </summary>
internal static class DefReferenceCheck
{
    // 由 LoadDataFromXmlCustom 以元素名（或 li 子元素）注册交叉引用的游戏类型 -> 接收引用的字段。
    private static readonly (string Type, string Field)[] CustomReferenceFields =
    [
        ("Verse.ThingDefCountClass", "thingDef"), ("Verse.ThingDefCountRangeClass", "thingDef"), ("RimWorld.StatModifier", "stat"),
        ("Verse.SkillRequirement", "skill"), ("RimWorld.SkillGain", "skill"), ("RimWorld.BackstoryTrait", "def"), ("Verse.TraitRequirement", "def"),
        ("Verse.DamageFactor", "damageDef"), ("RimWorld.PawnGenOption", "kind"), ("RimWorld.XenotypeChance", "xenotype"),
    ];

    public static void Run(Report report, ContentModel content)
    {
        report.Run("C.def-references", "Def cross-references resolve; optional content is referenced behind MayRequire", recorder => CheckReferences(recorder, content));
        report.Run("C.def-conflicts", "The mod defines each Def once and does not redefine game or dependency Defs", recorder => CheckConflicts(recorder, content));
    }

    private static void CheckReferences(CheckRecorder recorder, ContentModel content)
    {
        ContentPack mod = content.Catalog.UnderTest;
        string root = mod.RootDir;
        IReadOnlyList<string> minimalFolders = content.Catalog.FoldersFor(mod, content.Catalog.Minimal);
        IReadOnlyList<string> fullFolders = content.Catalog.FoldersFor(mod, content.Catalog.Full);
        Dictionary<Type, FieldInfo> customFields = ResolveCustomFields(recorder, content);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        int references = 0;

        void Check(XmlElement node, Type defType, string defName)
        {
            if (defName.Length == 0 || defName is "null" or "Null")
            {
                return;
            }

            references++;
            NodeSource? source = content.Corpus.SourceOf(node);
            string where = (source is null ? "" : XmlPaths.File(root, source.File) + " ") + XmlPaths.Of(node);
            (bool gated, string? inactive) = Gate(node, content.Catalog.Full);
            if (inactive is not null)
            {
                return;
            }

            bool conditionalFolder = source is not null && LoadFolderOf(source.File, fullFolders) is { } folder && !minimalFolders.Contains(folder, StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<DefEntry> found = content.Defs.Find(defType, defName);
            if (found.Count == 0)
            {
                IReadOnlyList<DefEntry> other = content.Defs.FindAny(defName);
                string detail = other.Count > 0 ? $"; '{defName}' is a {string.Join(", ", other.Select(e => e.Type.Name).Distinct())}" : "";
                if (reported.Add(where + "\t" + defName))
                {
                    recorder.Fail(where, $"Could not resolve cross-reference to {defType.Name} '{defName}'{detail} (the game logs an error and leaves the field null).");
                }

                return;
            }

            if (!gated && !conditionalFolder && !found.Any(entry => entry.InMinimal) && reported.Add(where + "\t" + defName))
            {
                string providers = string.Join(", ", found.Select(entry => entry.Pack.PackageId).Distinct());
                recorder.Fail(where, $"{defType.Name} '{defName}' exists only with optional content ({providers}); without it the reference fails. "
                    + "Add MayRequire to the node (or its list item), or move it into the conditional 1.7/Mods folder.");
            }
        }

        var walker = new XmlTypeWalker(content.Types, content.Catalog.Full)
        {
            DefReference = (node, _, defType, defName) => Check(node, defType, defName),
            CustomLoaded = (node, type) =>
            {
                if (customFields.TryGetValue(type, out FieldInfo? field))
                {
                    string name = node.Name != "li" ? node.Name : node[field.Name]?.InnerText.Trim() ?? node.InnerText.Trim();
                    Check(node, field.FieldType, name);
                }
            },
        };

        foreach (XmlElement node in content.Corpus.Defs)
        {
            NodeSource? source = content.Corpus.SourceOf(node);
            if (!ContentModel.IsOurs(source?.Pack) || source!.InsertedByPatch || !DefIndex.MayRequireSatisfied(node, content.Catalog.Full))
            {
                continue;
            }

            List<Type> inheritors = node.GetAttribute("Abstract").Equals("true", StringComparison.OrdinalIgnoreCase)
                ? content.Inheritance.Descendants(node).Select(walker.DefTypeOf).OfType<Type>().Distinct().ToList()
                : [];
            walker.WalkDef(node, inheritors);
        }

        // 补丁插入的 XML 在它最终所在的位置上检查。
        foreach (XmlElement node in content.Corpus.InsertedBy(mod))
        {
            if (node.ParentNode == content.Corpus.Root)
            {
                walker.WalkDef(node, []);
                continue;
            }

            (Type? declared, FieldInfo? field) = walker.DeclaredTypeOf(node, content.Corpus.Root);
            if (declared is not null)
            {
                walker.Walk(node, declared, field);
            }
        }

        recorder.Summary = $"{references} cross-reference(s) checked";
    }

    // 文件（Def 文件或补丁文件）所在的加载目录：包含它的最深的那个（1.7/Mods/VCookE 在 1.7 之内）。
    private static string? LoadFolderOf(string file, IReadOnlyList<string> folders)
    {
        string full = Path.GetFullPath(file);
        return folders.Where(folder => full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(folder => folder.Length)
            .FirstOrDefault();
    }

    // 节点或祖先上的 MayRequire / MayRequireAnyOf：gated 表示有保护；inactive 为当前配置中未启用的那个包 id（这时游戏跳过该节点）。
    private static (bool Gated, string? Inactive) Gate(XmlElement node, ActiveMods active)
    {
        bool gated = false;
        for (XmlNode? current = node; current is XmlElement element && element.ParentNode is not XmlDocument; current = current.ParentNode)
        {
            string all = element.GetAttribute("MayRequire");
            string any = element.GetAttribute("MayRequireAnyOf");
            if (all.Length + any.Length == 0)
            {
                continue;
            }

            gated = true;
            if (!DefIndex.MayRequireSatisfied(element, active))
            {
                return (true, all.Length > 0 ? all : any);
            }
        }

        return (gated, null);
    }

    private static Dictionary<Type, FieldInfo> ResolveCustomFields(CheckRecorder recorder, ContentModel content)
    {
        var result = new Dictionary<Type, FieldInfo>();
        foreach ((string typeName, string fieldName) in CustomReferenceFields)
        {
            Type? type = content.Types.GetTypeInAnyAssembly(typeName);
            FieldInfo? field = type?.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (type is null || field is null || !typeof(Def).IsAssignableFrom(field.FieldType))
            {
                recorder.Warn("table: " + typeName, $"The validator's custom cross-reference entry ({typeName}.{fieldName}) no longer matches the game; update DefReferenceCheck.");
                continue;
            }

            result[type] = field;
        }

        return result;
    }

    private static void CheckConflicts(CheckRecorder recorder, ContentModel content)
    {
        ContentPack mod = content.Catalog.UnderTest;
        string root = mod.RootDir;
        var earlier = new Dictionary<(Type, string), ContentPack>();
        var ours = new Dictionary<(Type, string), string>();
        int count = 0;
        foreach (XmlElement node in content.Corpus.Defs)
        {
            NodeSource? source = content.Corpus.SourceOf(node);
            string? defName = node["defName"]?.InnerText.Trim();
            if (source is null || source.InsertedByPatch || string.IsNullOrEmpty(defName)
                || node.GetAttribute("Abstract").Equals("true", StringComparison.OrdinalIgnoreCase) || !DefIndex.MayRequireSatisfied(node, content.Catalog.Full))
            {
                continue;
            }

            Type? type = content.Types.GetTypeInAnyAssembly(node.GetAttribute("Class") is { Length: > 0 } cls ? cls : node.Name);
            if (type is null || !typeof(Def).IsAssignableFrom(type) || type == typeof(Def))
            {
                continue;
            }

            (Type, string) key = (DefRoots.Database(type), defName);
            if (!ContentModel.IsOurs(source.Pack))
            {
                earlier.TryAdd(key, source.Pack);
                continue;
            }

            count++;
            string file = XmlPaths.File(root, source.File);
            if (ours.TryGetValue(key, out string? first))
            {
                recorder.Fail($"{key.Item1.Name}/{defName}", $"Defined twice by the mod ({first} and {file}); the later definition replaces the earlier one with an error.", file);
            }
            else
            {
                ours[key] = file;
            }

            if (earlier.TryGetValue(key, out ContentPack? owner))
            {
                recorder.Fail($"{key.Item1.Name}/{defName}", $"Also defined by {owner.PackageId}: the mod's definition replaces it for every player. Rename it, or change the original with a patch.", file);
            }
        }

        recorder.Summary = $"{count} concrete def(s) of the mod checked";
    }
}
