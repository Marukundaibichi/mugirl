using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace MugirlRace.Validation.Legacy;

/// <summary>
/// 流式读取真实存档（只读），只保留与 Mugirl 构建语料完全相同的名字（从不按子串 “Mugirl” 匹配，其他 Mod 也用这个词）：
/// <list type="bullet">
/// <item><c>type</c>：Class 值是某个旧构建定义的类型；<c>type?</c>：Class 值位于旧构建的命名空间中但没有任何构建定义它（验证构建）。</item>
/// <item><c>label</c>：本 Mod 类型节点（带 Class）的直接子节点名。</item>
/// <item><c>def</c>：叶子文本等于某个旧内容树的 defName；<c>thingid</c>：<c>[Thing_]&lt;ThingDef&gt;&lt;数字&gt;</c> 形式的 id。</item>
/// <item><c>def-pattern</c>：MooGirl 时代前缀（<c>MooGirl</c>/<c>MuGirl</c>）的叶子文本，即使没有构建定义它（git 之前的构建）。</item>
/// </list>
/// 不含本 Mod 包 id（<c>meta/modIds</c>）的存档整体跳过。
/// </summary>
internal sealed partial class SaveTokenScanner
{
    private readonly IReadOnlySet<string> knownTypes;
    private readonly IReadOnlySet<string> ownedNamespaces;
    private readonly IReadOnlySet<string> knownDefs;
    private readonly IReadOnlySet<string> knownThingDefs;

    public SaveTokenScanner(IReadOnlySet<string> knownTypes, IReadOnlySet<string> ownedNamespaces, IReadOnlySet<string> knownDefs, IReadOnlySet<string> knownThingDefs)
    {
        this.knownTypes = knownTypes;
        this.ownedNamespaces = ownedNamespaces;
        this.knownDefs = knownDefs;
        this.knownThingDefs = knownThingDefs;
    }

    /// <summary>本 Mod 的包 id（小写，存档中的写法）。</summary>
    public const string PackageId = "har.mugirlrace";

    public SaveScan Scan(string path)
    {
        var scan = new SaveScan();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true, IgnoreProcessingInstructions = true, IgnoreWhitespace = true };
        using XmlReader reader = XmlReader.Create(path, settings);
        var stack = new List<Frame>();
        var text = new StringBuilder();
        bool inMeta = false;
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                {
                    if (stack.Count > 0)
                    {
                        stack[^1].HasChildren = true;
                    }

                    string name = reader.Name;
                    string context = stack.Count > 0 ? stack[^1].Name + "/" + name : name;
                    if (stack.Count == 1 && name == "meta")
                    {
                        inMeta = true;
                    }

                    if (stack.Count == 1 && name == "game" && !scan.HasMod)
                    {
                        // 元数据之后才是游戏：没有本 Mod 的存档不再读下去。
                        return scan;
                    }

                    string? owner = null;
                    if (reader.GetAttribute("Class") is { Length: > 0 } cls)
                    {
                        foreach (string token in ContentSchema.TypeTokens(cls))
                        {
                            if (knownTypes.Contains(token))
                            {
                                scan.Add("type", token, context);
                                owner ??= token;
                            }
                            else if (Namespace(token) is { Length: > 0 } ns && ownedNamespaces.Contains(ns))
                            {
                                scan.Add("type?", token, context);
                                owner ??= token;
                            }
                        }
                    }

                    if (stack.Count > 0 && stack[^1].Owner is { } parentOwner)
                    {
                        scan.Add("label", name, parentOwner);
                    }

                    text.Clear();
                    if (reader.IsEmptyElement)
                    {
                        if (inMeta && stack.Count == 1)
                        {
                            inMeta = false;
                        }

                        continue;
                    }

                    stack.Add(new Frame(name, owner, context));
                    break;
                }

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                    text.Append(reader.Value);
                    break;
                case XmlNodeType.EndElement:
                {
                    Frame frame = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    if (!frame.HasChildren && text.Length > 0)
                    {
                        string value = text.ToString().Trim();
                        if (inMeta)
                        {
                            ReadMeta(scan, frame, value);
                        }
                        else
                        {
                            Classify(scan, value, frame.Context);
                        }
                    }

                    text.Clear();
                    if (inMeta && frame.Name == "meta")
                    {
                        inMeta = false;
                    }

                    break;
                }
            }
        }

        return scan;
    }

    private static void ReadMeta(SaveScan scan, Frame frame, string value)
    {
        if (frame.Name == "gameVersion")
        {
            scan.GameVersion = value;
        }
        else if (frame.Context == "modIds/li" && ActiveModsNormalize(value) == PackageId)
        {
            scan.HasMod = true;
        }
    }

    private static string ActiveModsNormalize(string id) => Content.ActiveMods.Normalize(id);

    private void Classify(SaveScan scan, string value, string context)
    {
        if (value.Length is 0 or > 200)
        {
            return;
        }

        if (knownDefs.Contains(value))
        {
            scan.Add("def", value, context);
            return;
        }

        if (ThingId().Match(value) is { Success: true } id)
        {
            // defName 本身可能以数字结尾：从最长的候选开始试每个切分点。
            string body = id.Groups["body"].Value;
            int digits = id.Groups["digits"].Length;
            for (int keep = digits - 1; keep >= 0; keep--)
            {
                string candidate = body + id.Groups["digits"].Value[..keep];
                if (knownThingDefs.Contains(candidate))
                {
                    scan.Add("thingid", candidate, id.Groups["thing"].Success ? "Thing_<id>" : "<id>");
                    return;
                }
            }
        }

        if (MooGirlName().IsMatch(value))
        {
            scan.Add("def-pattern", value, context);
        }
    }

    private static string Namespace(string type)
    {
        int dot = type.LastIndexOf('.');
        return dot < 0 ? "" : type[..dot];
    }

    [GeneratedRegex(@"^(?<thing>Thing_)?(?<body>[A-Za-z_][A-Za-z0-9_]*?[A-Za-z_])(?<digits>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ThingId();

    [GeneratedRegex(@"^(MooGirl|MuGirl)[A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex MooGirlName();

    private sealed class Frame(string name, string? owner, string context)
    {
        public string Name { get; } = name;

        public string? Owner { get; } = owner;

        public string Context { get; } = context;

        public bool HasChildren { get; set; }
    }
}
