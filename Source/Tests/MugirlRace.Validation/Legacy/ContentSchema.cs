using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MugirlRace.Validation.Legacy;

/// <summary>
/// 一个旧内容树（发布 ZIP、Git 提交或冻结的工作树）中与存档有关的事实。加载目录按该树自己的 LoadFolders.xml 的 1.6 列表
/// （所有 IfModActive 视为启用），高优先级目录遮蔽同相对路径的低优先级文件，同游戏。
/// </summary>
/// <remarks>
/// 记录：非抽象 Def 的数据库根与 defName（含游戏生成的 Corpse_/Meat_/Blueprint_/Frame_/Make_ 等）；Def 到 comp 的关系
/// （Def 自身、继承的父 Def、给 comps 加项的补丁）；XML 引用的本 Mod 类型；为各类字段写的值；全部 Keyed 键。
/// </remarks>
internal sealed partial class ContentSchema : IFieldValueSource
{
    private readonly Dictionary<(string Type, string Field), SortedSet<string>> fieldValues = [];

    private ContentSchema()
    {
    }

    /// <summary>（数据库根，defName，Def 类或 <c>-</c>）。</summary>
    public List<string[]> Defs { get; } = [];

    /// <summary>（数据库根，defName，Props 类，comp 类，来源 def|parent|patch）。</summary>
    public List<string[]> DefComps { get; } = [];

    /// <summary>（类型，用法：Class 特性所在元素、*Class 字段名或 defElement）。</summary>
    public List<string[]> XmlTypes { get; } = [];

    /// <summary>（ThingDef，工具 id）：动词加载 id 含工具 id（<c>&lt;ownerID&gt;_&lt;toolId&gt;_&lt;ManeuverDef&gt;</c>），所以它们也是存档名字。</summary>
    public List<string[]> Tools { get; } = [];

    /// <summary>全部语言的 Keyed 键。</summary>
    public HashSet<string> KeyedKeys { get; } = new(StringComparer.Ordinal);

    /// <summary>内容指纹：加载的每个 XML 文件（相对路径与 sha256）的哈希。</summary>
    public string ContentRef { get; private set; } = "-";

    /// <summary>About.xml 的 packageId（没有时为空）。</summary>
    public string PackageId { get; private set; } = "";

    /// <summary>无法解析的文件等问题（提取继续）。</summary>
    public List<string> Problems { get; } = [];

    public IEnumerable<string> ValuesOf(string typeName, string field)
        => fieldValues.TryGetValue((typeName, field), out SortedSet<string>? values) ? values : [];

    /// <summary>读取 <paramref name="root"/>。<paramref name="mod"/> 为同一构建的程序集（可为空），用于判断哪些类型属于本 Mod。</summary>
    public static ContentSchema Read(string root, string version, TypeUniverse universe, ModuleDefinition? mod)
    {
        var schema = new ContentSchema();
        string about = Path.Combine(root, "About", "About.xml");
        if (File.Exists(about))
        {
            try
            {
                var document = new XmlDocument();
                document.Load(about);
                schema.PackageId = document.DocumentElement?["packageId"]?.InnerText.Trim() ?? "";
            }
            catch (XmlException exception)
            {
                schema.Problems.Add("About/About.xml: " + exception.Message);
            }
        }

        IReadOnlyList<string> folders = LoadFolders(root, version);
        var fingerprint = new List<string>();
        var nodes = new List<DefNode>();
        foreach (LoadedFile file in LoadFolderResolver.Files(folders, "Defs", IsXml))
        {
            fingerprint.Add(Fingerprint(root, file));
            if (schema.Load(file.FullPath, root) is { DocumentElement: { } defs })
            {
                foreach (XmlElement element in defs.ChildNodes.OfType<XmlElement>())
                {
                    nodes.Add(new DefNode(element));
                }
            }
        }

        var patchRoots = new List<XmlElement>();
        foreach (LoadedFile file in LoadFolderResolver.Files(folders, "Patches", IsXml))
        {
            fingerprint.Add(Fingerprint(root, file));
            if (schema.Load(file.FullPath, root)?.DocumentElement is { } patch)
            {
                patchRoots.Add(patch);
            }
        }

        foreach (string folder in folders)
        {
            string languages = Path.Combine(folder, "Languages");
            if (!Directory.Exists(languages))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(languages, "*.xml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(languages, file).Replace('\\', '/');
                if (relative.Split('/') is [_, "Keyed", ..] && schema.Load(file, root)?.DocumentElement is { } keyed)
                {
                    fingerprint.Add(Fingerprint(root, new LoadedFile(folder, Path.GetRelativePath(folder, file), file)));
                    foreach (XmlElement key in keyed.ChildNodes.OfType<XmlElement>())
                    {
                        schema.KeyedKeys.Add(key.Name);
                    }
                }
            }
        }

        fingerprint.Sort(StringComparer.Ordinal);
        schema.ContentRef = fingerprint.Count == 0 ? "-" : LegacyHash.Sha256(string.Join("\n", fingerprint))[..16];
        schema.Analyze(nodes, patchRoots, universe, mod);
        return schema;
    }

    /// <summary>该树为 <paramref name="version"/> 加载的目录，优先级高的在前；条件目录全部视为启用。</summary>
    public static IReadOnlyList<string> LoadFolders(string root, string version)
    {
        ModLoadFolders? loadFolders = ModLoadFolders.TryRead(root);
        if (loadFolders is not null && loadFolders.DefinedVersions.Count > 0)
        {
            string? key = loadFolders.DefinedVersions.Contains(version)
                ? version
                : loadFolders.DefinedVersions
                    .Where(v => LoadFolderResolver.TryParse(v, out Version? parsed) && parsed <= Version.Parse(version))
                    .OrderByDescending(v => v, StringComparer.Ordinal)
                    .FirstOrDefault() ?? (loadFolders.DefinedVersions.Contains("default") ? "default" : null);
            if (key is not null && loadFolders.FoldersForVersion(key) is { } list)
            {
                return list.Reverse().Select(folder => Path.Combine(root, folder.FolderName)).Where(Directory.Exists).ToList();
            }
        }

        var folders = new List<string>();
        foreach (string candidate in new[] { Path.Combine(root, version), Path.Combine(root, "Common"), root })
        {
            if (Directory.Exists(candidate))
            {
                folders.Add(candidate);
            }
        }

        return folders;
    }

    private static string Fingerprint(string root, LoadedFile file)
        => Path.GetRelativePath(root, file.FullPath).Replace('\\', '/') + "\0" + LegacyHash.Sha256File(file.FullPath);

    private XmlDocument? Load(string path, string root)
    {
        try
        {
            var document = new XmlDocument();
            document.Load(path);
            return document;
        }
        catch (XmlException exception)
        {
            Problems.Add(Path.GetRelativePath(root, path).Replace('\\', '/') + ": " + exception.Message);
            return null;
        }
    }

    private void Analyze(List<DefNode> nodes, List<XmlElement> patchRoots, TypeUniverse universe, ModuleDefinition? mod)
    {
        var byName = new Dictionary<string, DefNode>(StringComparer.Ordinal);
        foreach (DefNode node in nodes.Where(n => n.Name.Length > 0))
        {
            byName.TryAdd(node.Name, node);
        }

        foreach (DefNode node in nodes)
        {
            node.Parent = node.ParentName.Length > 0 ? byName.GetValueOrDefault(node.ParentName) : null;
        }

        var rootByDefName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DefNode node in nodes)
        {
            CollectTypes(node.Element, mod, isDefElement: true);
            if (node.IsAbstract || node.DefName.Length == 0)
            {
                continue;
            }

            string typeName = node.Element.GetAttribute("Class") is { Length: > 0 } cls ? cls : node.Element.Name;
            TypeDefinition? type = universe.Find(typeName, mod);
            string root = (type is null ? null : TypeUniverse.DatabaseRoot(type)) ?? "?" + node.Element.Name;
            string defClass = type is not null && type.Name != root ? CecilNames.Type(type) : "-";
            Defs.Add([root, node.DefName, defClass]);
            rootByDefName.TryAdd(node.DefName, root);
            foreach ((string implied, string impliedRoot, string kind) in Implied(node, root))
            {
                Defs.Add([impliedRoot, implied, "implied:" + kind]);
            }

            foreach ((XmlElement item, string via) in EffectiveComps(node))
            {
                AddComp(root, node.DefName, item, via, universe, mod);
            }

            if (root == "ThingDef")
            {
                foreach (string tool in ToolIds(node))
                {
                    Tools.Add([node.DefName, tool]);
                }
            }
        }

        foreach (XmlElement patch in patchRoots)
        {
            CollectTypes(patch, mod, isDefElement: false);
            foreach (XmlElement operation in patch.GetElementsByTagName("xpath").OfType<XmlElement>().Select(x => x.ParentNode).OfType<XmlElement>())
            {
                string xpath = operation["xpath"]?.InnerText ?? "";
                if (operation["value"] is not { } value || !CompsTail().IsMatch(xpath))
                {
                    continue;
                }

                IEnumerable<XmlElement> items = value.ChildNodes.OfType<XmlElement>()
                    .SelectMany(child => child.Name == "comps" ? child.ChildNodes.OfType<XmlElement>() : [child])
                    .Where(child => child.Name == "li");
                List<string> defNames = DefNamePredicate().Matches(xpath).Select(m => m.Groups["name"].Value).Distinct(StringComparer.Ordinal).ToList();
                string? rootStep = RootStep().Match(xpath) is { Success: true } step && !step.Groups["root"].Value.Contains('*', StringComparison.Ordinal)
                    ? step.Groups["root"].Value
                    : null;
                foreach (XmlElement item in items)
                {
                    foreach (string defName in defNames.DefaultIfEmpty("*"))
                    {
                        string root = rootStep is not null && universe.Find(rootStep, mod) is { } rootType && TypeUniverse.DatabaseRoot(rootType) is { } resolved
                            ? resolved
                            : rootByDefName.GetValueOrDefault(defName) ?? "?" + (rootStep ?? "*");
                        AddComp(root, defName, item, "patch", universe, mod);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Def 节点（含继承的父节点，<c>&lt;tools Inherit="False"&gt;</c> 截断继承）的工具 id：<c>Tool.Id</c> 是 <c>id</c>，没有时是 <c>label</c>。
    /// </summary>
    public static IEnumerable<string> ToolIds(XmlElement? tools)
    {
        foreach (XmlElement item in tools?.ChildNodes.OfType<XmlElement>() ?? [])
        {
            string id = item["id"]?.InnerText.Trim() is { Length: > 0 } explicitId ? explicitId : item["label"]?.InnerText.Trim() ?? "";
            if (id.Length > 0)
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<string> ToolIds(DefNode node)
    {
        var seen = new HashSet<DefNode>();
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        for (DefNode? current = node; current is not null && seen.Add(current); current = current.Parent)
        {
            XmlElement? tools = current.Element["tools"];
            ids.UnionWith(ToolIds(tools));
            if (tools is not null && tools.GetAttribute("Inherit").Equals("False", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        return ids;
    }

    // comps 列表（与继承的父 Def 合并，<comps Inherit="False"> 截断继承）。
    private static IEnumerable<(XmlElement Item, string Via)> EffectiveComps(DefNode node)
    {
        var chain = new List<(DefNode Node, string Via)>();
        var seen = new HashSet<DefNode>();
        for (DefNode? current = node; current is not null && seen.Add(current); current = current.Parent)
        {
            chain.Add((current, current == node ? "def" : "parent"));
            if (current.Element["comps"] is { } comps && comps.GetAttribute("Inherit").Equals("False", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        foreach ((DefNode current, string via) in chain)
        {
            foreach (XmlElement item in current.Element["comps"]?.ChildNodes.OfType<XmlElement>() ?? [])
            {
                yield return (item, via);
            }
        }
    }

    private void AddComp(string root, string defName, XmlElement item, string via, TypeUniverse universe, ModuleDefinition? mod)
    {
        string propsName = item.GetAttribute("Class") is { Length: > 0 } cls ? cls : "-";
        TypeDefinition? props = propsName == "-" ? null : universe.Find(propsName, mod);
        string compName = item["compClass"]?.InnerText.Trim() is { Length: > 0 } explicitComp
            ? (universe.Find(explicitComp, mod) is { } resolvedComp ? CecilNames.Type(resolvedComp) : explicitComp)
            : props is null ? "-" : ConstructorCompClass(props) ?? "-";
        string propsFull = props is null ? propsName : CecilNames.Type(props);
        if (IsModType(propsFull, mod) || IsModType(compName, mod))
        {
            DefComps.Add([root, defName, propsFull, compName, via]);
        }
    }

    // CompProperties 构造函数中的 compClass = typeof(X)（沿本类型与基类的构造函数）。
    private static string? ConstructorCompClass(TypeDefinition props)
    {
        for (TypeDefinition? current = props; current is not null; current = CecilNames.TryResolve(current.BaseType))
        {
            foreach (MethodDefinition constructor in current.Methods.Where(m => m.IsConstructor && !m.IsStatic && m.HasBody))
            {
                foreach (Instruction instruction in constructor.Body.Instructions)
                {
                    if (instruction.OpCode.Code == Code.Stfld && instruction.Operand is FieldReference { Name: "compClass" })
                    {
                        for (Instruction? previous = instruction.Previous; previous is not null; previous = previous.Previous)
                        {
                            if (previous.OpCode.Code == Code.Ldtoken && previous.Operand is TypeReference token)
                            {
                                return CecilNames.Type(token);
                            }

                            if (previous.OpCode.Code == Code.Stfld)
                            {
                                break;
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    private static bool IsModType(string name, ModuleDefinition? mod)
        => mod is not null && name != "-" && mod.GetType(name.Replace('+', '/')) is not null;

    // 本 Mod 类型的 XML 引用与各类字段写的值。
    private void CollectTypes(XmlElement root, ModuleDefinition? mod, bool isDefElement)
    {
        if (isDefElement && root.Name.Contains('.', StringComparison.Ordinal) && IsModType(root.Name, mod))
        {
            XmlTypes.Add([root.Name, "defElement"]);
        }

        foreach (XmlElement element in Descendants(root))
        {
            string cls = element.GetAttribute("Class");
            if (cls.Length > 0)
            {
                foreach (string name in TypeTokens(cls))
                {
                    if (IsModType(name, mod))
                    {
                        XmlTypes.Add([name, "Class@" + (element.Name == "li" && element.ParentNode is XmlElement parent ? parent.Name + "/li" : element.Name)]);
                    }
                }

                foreach (XmlElement child in element.ChildNodes.OfType<XmlElement>().Where(c => !c.ChildNodes.OfType<XmlElement>().Any()))
                {
                    string text = child.InnerText.Trim();
                    if (text.Length > 0 && text.Length <= 200)
                    {
                        if (!fieldValues.TryGetValue((cls, child.Name), out SortedSet<string>? values))
                        {
                            fieldValues[(cls, child.Name)] = values = new SortedSet<string>(StringComparer.Ordinal);
                        }

                        values.Add(text);
                    }
                }
            }

            if ((element.Name.EndsWith("Class", StringComparison.Ordinal) || element.Name.EndsWith("Type", StringComparison.Ordinal))
                && !element.ChildNodes.OfType<XmlElement>().Any())
            {
                string text = element.InnerText.Trim();
                if (IsModType(text, mod))
                {
                    XmlTypes.Add([text, element.Name]);
                }
            }
        }
    }

    private static IEnumerable<XmlElement> Descendants(XmlElement root)
    {
        yield return root;
        foreach (XmlElement child in root.ChildNodes.OfType<XmlElement>())
        {
            foreach (XmlElement descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Class 值中的类型名（泛型实参展开，去掉程序集限定）。</summary>
    public static IEnumerable<string> TypeTokens(string value)
    {
        var builder = new StringBuilder();
        foreach (char c in value)
        {
            if (c is '[' or ']' or ',')
            {
                if (builder.Length > 0)
                {
                    yield return Clean(builder.ToString());
                    builder.Clear();
                }
            }
            else
            {
                builder.Append(c);
            }
        }

        if (builder.Length > 0)
        {
            yield return Clean(builder.ToString());
        }

        static string Clean(string token)
        {
            string trimmed = token.Trim();
            int tick = trimmed.IndexOf('`', StringComparison.Ordinal);
            return tick >= 0 ? trimmed[..tick] + "`" + new string(trimmed[(tick + 1)..].TakeWhile(char.IsAsciiDigit).ToArray()) : trimmed;
        }
    }

    // 游戏为 Def 生成的派生 Def（ThingDefGenerator_Corpses/_Meat/_Buildings、RecipeDefGenerator），按继承后的字段判断。
    private static IEnumerable<(string Name, string Root, string Kind)> Implied(DefNode node, string root)
    {
        if (root != "ThingDef")
        {
            yield break;
        }

        string defName = node.DefName;
        if (node.Has("race") && node.Value("category") == "Pawn" && node.Value("race/hasCorpse") != "false")
        {
            string? linkedCorpse = node.Value("race/linkedCorpseKind");
            if (string.IsNullOrEmpty(linkedCorpse) || linkedCorpse == defName)
            {
                yield return ("Corpse_" + defName, "ThingDef", "Corpse");
                // 历史语料记录全部 DLC 组合可能产生的名字；运行时内容目录才按 Anomaly 启用状态过滤。
                if (string.Equals(node.Value("race/hasUnnaturalCorpse"), "true", StringComparison.OrdinalIgnoreCase))
                {
                    yield return ("UnnaturalCorpse_" + defName, "ThingDef", "UnnaturalCorpse");
                }
            }

            if (node.Value("race/hasMeat") != "false" && !node.Has("race/useMeatFrom") && !node.Has("race/specificMeatDef") && node.Value("race/fleshType") != "Mechanoid")
            {
                yield return ("Meat_" + defName, "ThingDef", "Meat");
            }
        }

        if (!string.IsNullOrEmpty(node.Value("designationCategory")))
        {
            yield return ("Blueprint_" + defName, "ThingDef", "Blueprint");
            yield return ("Frame_" + defName, "ThingDef", "Frame");
        }

        if (!string.IsNullOrEmpty(node.Value("minifiedDef")))
        {
            yield return ("Blueprint_Install_" + defName, "ThingDef", "Blueprint_Install");
        }

        if (node.Has("recipeMaker"))
        {
            yield return ("Make_" + defName, "RecipeDef", "Make");
        }
    }

    private static bool IsXml(string extension) => string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"comps\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex CompsTail();

    [GeneratedRegex("""defName\s*=\s*["'](?<name>[^"']+)["']""", RegexOptions.CultureInvariant)]
    private static partial Regex DefNamePredicate();

    [GeneratedRegex(@"^/?Defs/(?<root>[A-Za-z0-9_.*]+)", RegexOptions.CultureInvariant)]
    private static partial Regex RootStep();

    /// <summary>Def XML 节点与其（同一内容树内的）父节点。</summary>
    private sealed class DefNode
    {
        public DefNode(XmlElement element)
        {
            Element = element;
            Name = element.GetAttribute("Name");
            ParentName = element.GetAttribute("ParentName");
            IsAbstract = element.GetAttribute("Abstract").Equals("true", StringComparison.OrdinalIgnoreCase);
            DefName = element["defName"]?.InnerText.Trim() ?? "";
        }

        public XmlElement Element { get; }

        public string Name { get; }

        public string ParentName { get; }

        public bool IsAbstract { get; }

        public string DefName { get; }

        public DefNode? Parent { get; set; }

        /// <summary>沿父链第一个有 <paramref name="path"/> 的节点上的文本。</summary>
        public string? Value(string path)
        {
            int depth = 0;
            for (DefNode? current = this; current is not null && depth++ < 32; current = current.Parent)
            {
                if (current.Element.SelectSingleNode(path) is XmlElement found)
                {
                    return found.InnerText.Trim();
                }
            }

            return null;
        }

        public bool Has(string path) => Value(path) is not null;
    }
}
