using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using MugirlRace.Validation.Legacy;
using Mono.Cecil;

namespace MugirlRace.Validation.Parity;

/// <summary>
/// 两棵内容树的一致性（1.7 加载列表，条件目录全部启用），按游戏读到的单位比较，文件拆分与移动不算差异：
/// <list type="bullet">
/// <item>Def：每个 Def 元素（按 Def 类型与 defName 或抽象 Name 标识）经改名映射后逐元素相同；每种 Def 类型的加载顺序相同（DefDatabase
/// 顺序影响遍历与随机选择）。</item>
/// <item>补丁：全部补丁操作按加载顺序组成的序列相同（xpath 中的 defName、Name、Class 字面量与类型段按映射改写）。</item>
/// <item>语言：每种语言的 Keyed 键与 DefInjected 键（defName 段与类型句柄按映射改写）到文本的映射相同。</item>
/// <item>资源：Textures 与 Sounds 下的文件集合相同。</item>
/// <item>同一侧内同名文件被高优先级加载目录遮蔽的也报告。</item>
/// </list>
/// 补丁应用后的 Def 结果需要游戏类型解析（完整验证程序的 C.* 检查与游戏内冒烟覆盖），这里不做。
/// </summary>
internal static partial class ContentParity
{
    public static List<ParityDiff> Compare(string rootA, string rootB, RenameMap map, TypeUniverse universe, ModuleDefinition moduleA, ModuleDefinition moduleB)
    {
        var diffs = new List<ParityDiff>();
        Tree a = Tree.Read(rootA, map, new XmlParitySchema(universe, moduleA), diffs, "A");
        Tree b = Tree.Read(rootB, null, new XmlParitySchema(universe, moduleB), diffs, "B");
        CompareKeyed(diffs, "def", a.Defs, b.Defs);
        foreach (string type in a.DefOrder.Keys.Union(b.DefOrder.Keys).Order(StringComparer.Ordinal))
        {
            List<string> orderA = a.DefOrder.GetValueOrDefault(type) ?? [];
            List<string> orderB = b.DefOrder.GetValueOrDefault(type) ?? [];
            List<string> common = orderA.Intersect(orderB, StringComparer.Ordinal).ToList();
            if (!orderA.Where(common.Contains).SequenceEqual(orderB.Where(common.Contains), StringComparer.Ordinal))
            {
                diffs.Add(new ParityDiff("def-order", type, FirstDifference(string.Join(",", orderA.Where(common.Contains)), string.Join(",", orderB.Where(common.Contains)))));
            }
        }

        if (!a.Patches.SequenceEqual(b.Patches, StringComparer.Ordinal))
        {
            var countsB = b.Patches.GroupBy(p => p, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            var countsA = a.Patches.GroupBy(p => p, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach ((string operation, int count) in countsA.Where(pair => pair.Value > countsB.GetValueOrDefault(pair.Key)))
            {
                diffs.Add(new ParityDiff("patch", Abbreviate(operation), $"Operation missing in B ({count - countsB.GetValueOrDefault(operation)} of {count})."));
            }

            foreach ((string operation, int count) in countsB.Where(pair => pair.Value > countsA.GetValueOrDefault(pair.Key)))
            {
                diffs.Add(new ParityDiff("patch", Abbreviate(operation), $"Operation only in B ({count - countsA.GetValueOrDefault(operation)} of {count})."));
            }

            if (diffs.All(diff => diff.Category != "patch"))
            {
                diffs.Add(new ParityDiff("patch-order", "Patches", "The same operations apply in a different order: " + FirstDifference(string.Join("\n", a.Patches), string.Join("\n", b.Patches))));
            }
        }

        CompareKeyed(diffs, "language", a.Language, b.Language);
        foreach (string asset in a.Assets.Except(b.Assets, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            diffs.Add(new ParityDiff("asset", asset, "Texture or sound missing in B."));
        }

        foreach (string asset in b.Assets.Except(a.Assets, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            diffs.Add(new ParityDiff("asset", asset, "Texture or sound only in B."));
        }

        return diffs;
    }

    private static void CompareKeyed(List<ParityDiff> diffs, string category, Dictionary<string, string> a, Dictionary<string, string> b)
    {
        foreach ((string key, string valueA) in a.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!b.TryGetValue(key, out string? valueB))
            {
                diffs.Add(new ParityDiff(category, key, "Missing in B."));
            }
            else if (valueA != valueB)
            {
                diffs.Add(new ParityDiff(category, key, FirstDifference(valueA, valueB)));
            }
        }

        foreach (string key in b.Keys.Where(key => !a.ContainsKey(key)).Order(StringComparer.Ordinal))
        {
            diffs.Add(new ParityDiff(category, key, "Only in B."));
        }
    }

    private static string Abbreviate(string text) => text.Length <= 160 ? text : text[..160] + "…";

    private static string FirstDifference(string a, string b)
    {
        int index = 0;
        while (index < a.Length && index < b.Length && a[index] == b[index])
        {
            index++;
        }

        int start = Math.Max(0, index - 60);
        return $"A: …{a[start..Math.Min(a.Length, index + 80)]}…  B: …{b[start..Math.Min(b.Length, index + 80)]}…";
    }

    // xpath 中的引号字面量（defName、Name、Class 的值）。
    [GeneratedRegex("""(?<q>["'])(?<value>[^"']*)\k<q>""", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedLiteral();

    /// <summary>一侧内容树中游戏读到的单位（A 侧已按映射改写）。</summary>
    private sealed class Tree
    {
        private readonly RenameMap? map;

        private readonly XmlParitySchema schema;
        private Dictionary<XmlElement, XmlParitySchema.Slot> slots = [];

        private Tree(RenameMap? map, XmlParitySchema schema)
        {
            this.map = map;
            this.schema = schema;
        }

        public Dictionary<string, string> Defs { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<string>> DefOrder { get; } = new(StringComparer.Ordinal);

        public List<string> Patches { get; } = [];

        public Dictionary<string, string> Language { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Assets { get; } = new(StringComparer.Ordinal);

        public static Tree Read(string root, RenameMap? map, XmlParitySchema schema, List<ParityDiff> diffs, string side)
        {
            var tree = new Tree(map, schema);
            var loadRelative = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string folder in ContentSchema.LoadFolders(root, "1.7"))
            {
                foreach (string sub in new[] { "Defs", "Patches", "Languages", "Textures", "Sounds" })
                {
                    string directory = Path.Combine(folder, sub);
                    if (!Directory.Exists(directory))
                    {
                        continue;
                    }

                    // DirectXmlLoader/GetAllFilesForModPreserveOrder 保留文件系统枚举顺序，不按名字排序。
                    foreach (string file in new DirectoryInfo(directory).GetFiles("*", SearchOption.AllDirectories).Select(f => f.FullName))
                    {
                        string inFolder = Path.GetRelativePath(folder, file).Replace('\\', '/');
                        string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                        if (sub is "Textures" or "Sounds")
                        {
                            tree.Assets.Add(inFolder);
                            continue;
                        }

                        if (!file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (sub != "Languages" && !loadRelative.TryAdd(inFolder, relative))
                        {
                            diffs.Add(new ParityDiff("content-shadowing", relative, $"{side}: shadowed by {loadRelative[inFolder]} (the game drops it)."));
                            continue;
                        }

                        tree.ReadFile(file, sub, inFolder, diffs, relative);
                    }
                }
            }

            return tree;
        }

        private void ReadFile(string path, string sub, string inFolder, List<ParityDiff> diffs, string relative)
        {
            var document = new XmlDocument { PreserveWhitespace = false };
            try
            {
                document.Load(path);
            }
            catch (XmlException exception)
            {
                diffs.Add(new ParityDiff("content", relative, "Malformed XML: " + exception.Message));
                return;
            }

            if (document.DocumentElement is not { } top)
            {
                return;
            }

            switch (sub)
            {
                case "Defs":
                    foreach (XmlElement def in top.ChildNodes.OfType<XmlElement>())
                    {
                        slots = schema.Analyze(def);
                        string type = MapTypeName(def.Name);
                        string databaseRoot = schema.RootOf(def) ?? def.Name[(def.Name.LastIndexOf('.') + 1)..];
                        string identity = def["defName"]?.InnerText.Trim() is { Length: > 0 } defName ? MapDef(defName, databaseRoot)
                            : def.GetAttribute("Name") is { Length: > 0 } name ? "Name=" + (map?.AbstractName(name) ?? name)
                            : "#" + inFolder;
                        string key = type + "/" + identity;
                        string text = Canonical(def, databaseRoot);
                        if (!Defs.TryAdd(key, text))
                        {
                            Defs[key + "#" + Defs.Count] = text;
                        }

                        if (!DefOrder.TryGetValue(type, out List<string>? order))
                        {
                            DefOrder[type] = order = [];
                        }

                        order.Add(identity);
                    }

                    break;
                case "Patches":
                    slots = [];
                    foreach (XmlElement operation in top.ChildNodes.OfType<XmlElement>())
                    {
                        slots = schema.AnalyzePatch(operation);
                        Patches.Add(Canonical(operation, null));
                    }

                    break;
                default:
                    // Languages/<语言>/(Keyed|DefInjected/<Def 类型>)/…：键按语言与种类区分，文件拆分不改变键。
                    string[] parts = inFolder.Split('/');
                    string language = parts.Length > 1 ? parts[1] : "";
                    string kind = parts.Length > 2 ? parts[2] + (parts[2] == "DefInjected" && parts.Length > 3 ? "/" + MapTypeName(parts[3]) : "") : "";
                    foreach (XmlElement entry in top.ChildNodes.OfType<XmlElement>())
                    {
                        string key = $"{language}/{kind}/{MapLanguageKey(entry.Name, parts.Length > 3 && parts[2] == "DefInjected" ? parts[3] : null)}";
                        // 仅键使用改名映射；翻译值（含空白、内嵌标记）逐字比较，不能被标识符映射掩盖。
                        string text = entry.InnerXml;
                        if (!Language.TryAdd(key, text))
                        {
                            Language[key + "#" + Language.Count] = text;
                        }
                    }

                    break;
            }
        }

        private string MapTypeName(string name) => map is null || !name.Contains('.', StringComparison.Ordinal) ? name : map.Type(name);

        private string MapDef(string name, string? root) => map?.Def(name, root?[(root.LastIndexOf('.') + 1)..]) ?? name;

        // Keyed 键整体映射；DefInjected 键的第一段是 defName，其余段中的类型句柄（短类名）按类型映射。
        private string MapLanguageKey(string key, string? defRoot)
        {
            if (map is null)
            {
                return key;
            }

            if (defRoot is null)
            {
                return map.Key(key);
            }

            string[] segments = key.Split('.');
            segments[0] = map.Def(segments[0], defRoot);
            for (int i = 1; i < segments.Length; i++)
            {
                segments[i] = map.ShortType(segments[i]);
            }

            return string.Join(".", segments);
        }

        // 规范 XML：去掉注释与纯空白，属性按名排序；A 侧把名字改写成 B 的。
        private string Canonical(XmlElement element, string? defType)
        {
            var text = new StringBuilder();
            Write(element, text, defType, isRoot: true);
            return text.ToString();
        }

        private void Write(XmlElement element, StringBuilder text, string? defType, bool isRoot)
        {
            string name = isRoot ? MapTypeName(element.Name) : element.Name;
            slots.TryGetValue(element, out XmlParitySchema.Slot? slot);
            if (map is not null && slot?.ElementDefRoot is { } elementRoot) { name = map.Def(name, elementRoot); }
            text.Append('<').Append(name);
            foreach (XmlAttribute attribute in element.Attributes.OfType<XmlAttribute>().OrderBy(a => a.Name, StringComparer.Ordinal))
            {
                string value = attribute.Value;
                if (map is not null)
                {
                    value = attribute.Name switch
                    {
                        "Class" => MapTypes(value),
                        "Name" or "ParentName" => map.AbstractName(value),
                        _ => value,
                    };
                }

                text.Append(' ').Append(attribute.Name).Append("=\"").Append(value).Append('"');
            }

            text.Append('>');
            foreach (XmlNode child in element.ChildNodes)
            {
                switch (child)
                {
                    case XmlElement nested:
                        Write(nested, text, defType, isRoot: false);
                        break;
                    case XmlText or XmlCDataSection:
                        bool playerText = IsPlayerText(element);
                        string value = playerText ? child.Value ?? "" : child.Value?.Trim() ?? "";
                        if (map is not null && !playerText)
                        {
                            value = element.Name switch
                            {
                                "xpath" => MapXpath(value),
                                "defName" when isRoot is false && element.ParentNode is XmlElement { ParentNode: XmlElement { Name: "Defs" } } => MapDef(value, defType),
                                _ when slot?.DefRoot is { } referenceRoot => map.Def(value, referenceRoot),
                                _ when slot?.IsType == true => MapTypes(value),
                                _ => value,
                            };
                        }

                        text.Append(value);
                        break;
                }
            }

            text.Append("</").Append(name).Append('>');
        }

        // 原文可能恰好等于某个旧 defName/类名；这仍然是玩家文案，不能按标识符归一化。
        private static bool IsPlayerText(XmlElement element)
        {
            for (XmlElement? current = element; current is not null; current = current.ParentNode as XmlElement)
            {
                string name = current.Name;
                if (name.EndsWith("Key", StringComparison.Ordinal) || name.EndsWith("Keys", StringComparison.Ordinal)) { continue; }
                if (name is "label" or "labelPlural" or "labelShort" or "description" or "descriptionDetailed" or "reportString" or "jobString"
                    or "text" or "title" or "titleShort" or "letterLabel" or "letterText" or "letterTitle" or "successMessage" or "failMessage"
                    or "deathMessage" or "pawnLabel" or "questName" or "questDescription" or "gerund"
                    || name.EndsWith("Label", StringComparison.Ordinal) || name.EndsWith("Description", StringComparison.Ordinal)
                    || name.EndsWith("Text", StringComparison.Ordinal) || name.EndsWith("Title", StringComparison.Ordinal)
                    || name.EndsWith("ReportString", StringComparison.Ordinal) || name.EndsWith("Message", StringComparison.Ordinal))
                { return true; }
            }

            return false;
        }

        private string MapTypes(string value)
            => string.Join(",", value.Split(',').Select(part => map!.Type(part.Trim()) is var mapped && mapped != part.Trim() ? mapped : part));

        // xpath：引号字面量（defName、Name、Class 的值）与含点的路径段（Def 类元素名）按映射改写。
        private string MapXpath(string xpath) => schema.MapXPath(xpath, map!);
    }
}
