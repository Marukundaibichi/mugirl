using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Metadata;
using MugirlRace.Validation.Reporting;
using MugirlRace.Validation.Runtime;
using Verse;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// C. 每个 DefInjected 键路径都能落到目标 Def 上的一个字段（<c>DefInjectionPackage.SetDefFieldAtPath</c> 的规则）：字段名（忽略大小写，
/// 含 LoadAlias）、列表下标、列表句柄（元素的 <c>[TranslationHandle]</c> 字段值，例如 comps 的 <c>compClass</c> 短名
/// <c>CompUsable_RopeToHitch</c>，<c>-N</c> 区分同名句柄）。注入失败不会写日志，只进翻译报告，所以类名或字段改名后的句柄会静默失效。
/// </summary>
/// <remarks>
/// 按继承与补丁之后的 Def XML 求值；句柄字段在 XML 中没写时，从元素类型构造函数的 IL 读取它设置的常量（CompProperties 的
/// <c>compClass = typeof(X)</c>），不运行 Mod 代码。
/// Def 不存在的键由 <c>C.language-files</c> 报告，这里跳过。
/// </remarks>
internal static partial class DefInjectedPathCheck
{
    private const BindingFlags FieldFlags = BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Run(Report report, MetadataWorld world, ContentModel content)
        => report.Run("C.definjected-paths", "Every DefInjected key path resolves to a field of the def (incl. list handles)", recorder => Check(recorder, world, content));

    private static void Check(CheckRecorder recorder, MetadataWorld world, ContentModel content)
    {
        var handles = new HandleDefaults(world);
        ContentPack mod = content.Catalog.UnderTest;
        Dictionary<string, List<XmlElement>> defsByName = content.Corpus.Defs
            .Where(node => node["defName"] is not null && !node.GetAttribute("Abstract").Equals("true", StringComparison.OrdinalIgnoreCase))
            .GroupBy(node => node["defName"]!.InnerText.Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        int keys = 0;
        int unresolvedTypes = 0;
        foreach (string folder in content.Catalog.FoldersFor(mod, content.Catalog.Full))
        {
            string languages = Path.Combine(folder, "Languages");
            if (!Directory.Exists(languages))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(languages, "*.xml", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string[] parts = Path.GetRelativePath(languages, file).Replace('\\', '/').Split('/');
                if (parts.Length < 4 || parts[1] != "DefInjected")
                {
                    continue;
                }

                if (content.Types.GetTypeInAnyAssembly(parts[2]) is not { } defType || !typeof(Def).IsAssignableFrom(defType))
                {
                    unresolvedTypes++;
                    continue;
                }

                XmlElement? root = TryLoad(file);
                string location = XmlPaths.File(mod.RootDir, file);
                foreach (XmlElement element in root?.ChildNodes.OfType<XmlElement>() ?? [])
                {
                    if (element.Name == "rep")
                    {
                        continue;
                    }

                    string path = element.Name.Replace("]", "", StringComparison.Ordinal).Replace('[', '.');
                    string[] segments = path.Split('.');
                    if (segments.Length < 2 || !defsByName.TryGetValue(segments[0], out List<XmlElement>? candidates))
                    {
                        continue;
                    }

                    XmlElement? node = candidates.FirstOrDefault(c => content.Types.GetTypeInAnyAssembly(c.GetAttribute("Class") is { Length: > 0 } cls ? cls : c.Name) is { } t && defType.IsAssignableFrom(t));
                    if (node is null)
                    {
                        continue;
                    }

                    keys++;
                    Type runtimeType = content.Types.GetTypeInAnyAssembly(node.GetAttribute("Class") is { Length: > 0 } c2 ? c2 : node.Name) ?? defType;
                    bool fullList = element.ChildNodes.OfType<XmlElement>().Any();
                    if (Resolve(content, handles, runtimeType, content.Inheritance.Resolve(node), segments, fullList) is { } problem)
                    {
                        recorder.Fail($"{parts[0]}/{parts[2]}: {element.Name}", "DefInjected path does not resolve: " + problem + " The injection is silently skipped (only the translation report shows it).", location);
                    }
                }
            }
        }

        if (unresolvedTypes > 0)
        {
            recorder.Info("(def type folders)", $"{unresolvedTypes} DefInjected file(s) in folders that name no loaded Def type (reported by C.language-files when the mod defines them).");
        }

        recorder.Summary = $"{keys} DefInjected key path(s) resolved";
    }

    // 逐段解析；返回失败原因或 null。
    private static string? Resolve(ContentModel content, HandleDefaults handles, Type defType, XmlElement defNode, string[] segments, bool fullList)
    {
        Type current = defType;
        XmlElement? node = defNode;
        for (int i = 1; i < segments.Length; i++)
        {
            string segment = segments[i];
            bool last = i == segments.Length - 1;
            Type? element = ListElement(current);
            if (int.TryParse(segment, out int index))
            {
                if (element is null)
                {
                    return $"'{segment}' indexes {Describe(current)}, which is not a list.";
                }

                // 空列表或 XML 中没有的列表可能在加载时由代码填充（ThinkNode_Subtree 的子节点、HAR 的 conditions）：无法核对。
                List<XmlElement>? items = node?.ChildNodes.OfType<XmlElement>().Where(e => e.Name == "li").ToList();
                if (items is null || items.Count == 0)
                {
                    return null;
                }

                if (index >= items.Count)
                {
                    return $"index {index} is out of range (the list has {items.Count} item(s)).";
                }

                node = items[index];
                current = ItemType(content, element, node);
                if (last && current != typeof(string))
                {
                    return $"the path ends at a list item of type {Describe(current)}, not a string.";
                }

                continue;
            }

            if (Field(current, segment) is { } field)
            {
                node = node?.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.Name.Equals(field.Name, StringComparison.OrdinalIgnoreCase));
                current = node?.GetAttribute("Class") is { Length: > 0 } cls && content.Types.GetTypeInAnyAssembly(cls) is { } derived && field.FieldType.IsAssignableFrom(derived) ? derived : field.FieldType;
                if (last && current != typeof(string) && !(fullList && ListElement(current) == typeof(string)))
                {
                    return $"the path ends at field {field.Name} of type {Describe(current)}; only string fields (or string lists with <li> items) can be translated.";
                }

                continue;
            }

            if (node is null)
            {
                // XML 中没有这一段：运行时类型可能是声明类型的子类，无法核对字段。
                return null;
            }

            if (element is null)
            {
                return $"{Describe(current)} has no field '{segment}'.";
            }

            // 列表句柄：元素的 [TranslationHandle] 字段值（类型取短名），-N 区分同名句柄。
            string handle = segment;
            int handleIndex = 0;
            int dash = segment.LastIndexOf('-');
            if (dash > 0 && int.TryParse(segment[(dash + 1)..], out int parsed))
            {
                handle = segment[..dash];
                handleIndex = parsed;
            }

            if (node is null)
            {
                return null;
            }

            List<XmlElement> matches = node.ChildNodes.OfType<XmlElement>().Where(e => e.Name == "li" && Handles(content, handles, element, e).Any(h => HandlesMatch(h, handle))).ToList();
            if (handleIndex >= matches.Count)
            {
                return matches.Count == 0
                    ? $"no item of {Describe(current)} has the handle '{handle}' (a renamed class or changed handle field?)."
                    : $"handle '{handle}' has only {matches.Count} match(es); index {handleIndex} does not exist.";
            }

            node = matches[handleIndex];
            current = ItemType(content, element, node);
        }

        return null;
    }

    private static FieldInfo? Field(Type type, string name)
    {
        FieldInfo? field = type.GetField(name, FieldFlags);
        if (field is not null)
        {
            return field;
        }

        foreach (FieldInfo candidate in type.GetFields(FieldFlags))
        {
            if (candidate.GetCustomAttributes(typeof(LoadAliasAttribute), inherit: false).OfType<LoadAliasAttribute>().Any(alias => alias.alias == name))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Type? ListElement(Type type)
        => type.IsGenericType && typeof(IList).IsAssignableFrom(type) ? type.GetGenericArguments()[0] : null;

    private static Type ItemType(ContentModel content, Type element, XmlElement? item)
        => item?.GetAttribute("Class") is { Length: > 0 } cls && content.Types.GetTypeInAnyAssembly(cls) is { } derived && element.IsAssignableFrom(derived) ? derived : element;

    // 一个列表项的句柄值：每个 [TranslationHandle] 字段在 XML 中的文本，或（XML 没写时）构造函数设置的常量。
    private static IEnumerable<string> Handles(ContentModel content, HandleDefaults defaults, Type element, XmlElement item)
    {
        Type type = ItemType(content, element, item);
        if (type == typeof(string))
        {
            yield return item.InnerText;
            yield break;
        }

        foreach (FieldInfo field in type.GetFields(FieldFlags).Where(f => f.GetCustomAttributes(inherit: true).Any(a => a.GetType().Name == "TranslationHandleAttribute")))
        {
            // untranslatedX 由游戏在加载时从 XML 字段 x 复制（例如 BodyPartRecord.untranslatedCustomLabel ← customLabel）。
            string xmlName = field.Name.StartsWith("untranslated", StringComparison.Ordinal) && field.Name.Length > "untranslated".Length
                ? char.ToLowerInvariant(field.Name["untranslated".Length]) + field.Name[("untranslated".Length + 1)..]
                : field.Name;
            if (item.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.Name.Equals(field.Name, StringComparison.OrdinalIgnoreCase) || e.Name.Equals(xmlName, StringComparison.OrdinalIgnoreCase)) is { } written)
            {
                string text = written.InnerText.Trim();
                yield return text;
                if (field.FieldType == typeof(Type) && content.Types.GetTypeInAnyAssembly(text) is { } resolved)
                {
                    yield return resolved.Name;
                }

                continue;
            }

            foreach (string value in defaults.Of(type, field.Name))
            {
                yield return value;
            }
        }
    }

    private static bool HandlesMatch(string value, string handle) => NormalizedHandle(value) is { Length: > 0 } normalized && normalized == NormalizedHandle(handle);

    // TranslationHandleUtility.NormalizedHandle。
    private static string NormalizedHandle(string handle)
    {
        string text = handle.Trim().Replace(' ', '_').Replace('\n', '_').Replace("\r", "", StringComparison.Ordinal).Replace('\t', '_')
            .Replace(".", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        text = FormatSymbols().Replace(text, "");
        var builder = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            {
                if (!(c == '_' && builder.Length > 0 && builder[^1] == '_'))
                {
                    builder.Append(c);
                }
            }
        }

        string result = builder.ToString().Trim('_');
        return result.Length > 0 && result.All(char.IsAsciiDigit) ? "_" + result : result;
    }

    private static string Describe(Type type) => RuntimeNames.Type(type);

    private static XmlElement? TryLoad(string file)
    {
        try
        {
            var document = new XmlDocument();
            document.Load(file);
            return document.DocumentElement;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    [GeneratedRegex("{.*?}", RegexOptions.CultureInvariant)]
    private static partial Regex FormatSymbols();
}
