using MugirlRace.Validation.Legacy;

namespace MugirlRace.Validation.SaveUpgrade;

/// <summary>
/// 升级清单中的所属选择器文本（契约 K1，升级器 <c>OwnerSelector.Text</c> 的语法）对应的旧 owner 类型：语料按旧类型记录标签，清单按
/// 新类型选元素。
/// </summary>
/// <remarks>
/// <para>
/// 语法：<c>Any</c>、<c>Class(a|b)</c>、<c>GameComponent(X)</c>、<c>MapComponent(X)</c>、<c>WorldComponent(X)</c>、<c>ThingComp(X)</c>、
/// <c>HediffComp(X)</c>、<c>Path(&lt;选择器&gt;, a/li/b)</c>、<c>Document(a/li/b)</c>；类型名是映射后的存档写法，与游戏一样不区分大小写。
/// 设计稿的旧写法（<c>ClassIs</c>、<c>ThingWithComp</c>、<c>HediffWithComp</c>、<c>PathFrom(X, "a/li")</c>）同样接受。
/// </para>
/// <para>
/// 类型名选中映射到它的每个旧类型；标签声明在旧基类上时，基类的每个具体子类都被选中才算选中基类。路径沿语料的存档图
/// （legacy-schema-graph.tsv，Deep 保存的本 Mod 类型）前进，<c>li</c>、<c>keys</c>、<c>values</c> 是集合结构，<c>*</c> 匹配任意标签。
/// <c>Document</c> 从 <c>game</c> 元素出发、经过原版元素，图只能按最后一个标签段找到元素类型；此外选中把标签写进原版元素的静态类
/// （例如 ExposeData 补丁）。
/// </para>
/// </remarks>
internal sealed class OwnerResolver
{
    private static readonly string[] Structural = ["li", "keys", "values"];

    private readonly Dictionary<string, List<string>> legacyByNewName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILookup<string, CorpusRow> graph;
    private readonly Dictionary<string, string> modBase = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> subclasses = new(StringComparer.Ordinal);
    private readonly HashSet<string> abstractTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> staticTypes = new(StringComparer.Ordinal);

    /// <param name="corpus">旧语料（类型、存档图）。</param>
    /// <param name="legacyTypes">全部旧类型名（构建与存档）。</param>
    /// <param name="mapType">旧类型名的新名（丢弃或被消费时为 <see langword="null"/>）。</param>
    public OwnerResolver(LegacyCorpus corpus, IEnumerable<string> legacyTypes, Func<string, string?> mapType)
    {
        graph = corpus.Rows(LegacyCorpus.GraphFile).ToLookup(r => r[0], StringComparer.Ordinal);
        foreach (CorpusRow row in corpus.Rows(LegacyCorpus.TypesFile))
        {
            if (row[3] != "-")
            {
                modBase.TryAdd(row[0], row[3]);
            }

            string flags = row[5];
            if (flags.Split(',').Contains("static"))
            {
                staticTypes.Add(row[0]);
            }
            else if (flags.Split(',').Contains("abstract") || row[1] == "interface")
            {
                abstractTypes.Add(row[0]);
            }
        }

        foreach (string type in modBase.Keys)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (string? current = modBase.GetValueOrDefault(type); current is not null && seen.Add(current); current = modBase.GetValueOrDefault(current))
            {
                if (!subclasses.TryGetValue(current, out HashSet<string>? set))
                {
                    subclasses[current] = set = new HashSet<string>(StringComparer.Ordinal);
                }

                set.Add(type);
            }
        }

        foreach (string legacy in legacyTypes.Distinct(StringComparer.Ordinal))
        {
            if (mapType(legacy) is { } mapped)
            {
                if (!legacyByNewName.TryGetValue(mapped, out List<string>? list))
                {
                    legacyByNewName[mapped] = list = [];
                }

                list.Add(legacy);
            }
        }
    }

    /// <summary>
    /// 选择器 <paramref name="selector"/> 选中的旧 owner（写标签 <paramref name="label"/> 的类型）；无法解析时返回问题说明。
    /// </summary>
    public (IReadOnlySet<string> Owners, string? Problem) Resolve(string selector, string? label)
    {
        try
        {
            HashSet<string> owners = Owners(selector.Trim(), label);
            return (owners, null);
        }
        catch (FormatException exception)
        {
            return (new HashSet<string>(StringComparer.Ordinal), exception.Message);
        }
    }

    /// <summary><paramref name="type"/> 的本 Mod 祖先（由近到远）。</summary>
    public IEnumerable<string> Ancestors(string type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? current = modBase.GetValueOrDefault(type); current is not null && seen.Add(current); current = modBase.GetValueOrDefault(current))
        {
            yield return current;
        }
    }

    /// <summary>抽象标签声明者移除后，必须逐一核对语料中所有可实例化后代，不能把抽象声明当作数据丢弃。</summary>
    public IReadOnlyList<string> ConcreteDescendantsOfAbstract(string type) => abstractTypes.Contains(type)
        && subclasses.TryGetValue(type, out HashSet<string>? descendants)
        ? descendants.Where(child => !abstractTypes.Contains(child) && !staticTypes.Contains(child)).Order(StringComparer.Ordinal).ToArray()
        : [];

    private HashSet<string> Owners(string selector, string? label)
    {
        if (selector == "Any")
        {
            throw new FormatException("'Any' selects every element; a label row needs a concrete owner.");
        }

        int open = selector.IndexOf('(', StringComparison.Ordinal);
        if (open <= 0 || !selector.EndsWith(')'))
        {
            throw new FormatException($"'{selector}' is not a selector (Class(..), GameComponent(..), ThingComp(..), Path(.., ..), Document(..)).");
        }

        string kind = selector[..open].Trim();
        string arguments = selector[(open + 1)..^1].Trim();
        switch (kind)
        {
            case "Class" or "ClassIs":
                return Covering(arguments.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            case "GameComponent" or "MapComponent" or "WorldComponent" or "ThingComp" or "HediffComp" or "ThingWithComp" or "HediffWithComp":
                return Covering([arguments]);
            case "Path" or "PathFrom":
            {
                int comma = TopLevelComma(arguments);
                if (comma <= 0)
                {
                    throw new FormatException($"'{selector}' needs a root selector and a relative path.");
                }

                string path = arguments[(comma + 1)..].Trim().Trim('"');
                return Follow(Owners(arguments[..comma].Trim(), null), Segments(path, selector));
            }

            case "Document":
                return Document(Segments(arguments.Trim('"'), selector), label);
            default:
                throw new FormatException($"'{selector}': unknown selector kind '{kind}'.");
        }
    }

    // 映射到这些新名的旧类型，加上每个具体子类都被选中的旧基类。
    private HashSet<string> Covering(IEnumerable<string> newNames)
    {
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in newNames)
        {
            owners.UnionWith(legacyByNewName.GetValueOrDefault(name) ?? []);
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach ((string ancestor, HashSet<string> below) in subclasses)
            {
                if (owners.Contains(ancestor))
                {
                    continue;
                }

                List<string> concrete = below.Where(type => !abstractTypes.Contains(type) && !staticTypes.Contains(type)).ToList();
                if (concrete.Count > 0 && concrete.All(owners.Contains) && abstractTypes.Contains(ancestor))
                {
                    owners.Add(ancestor);
                    changed = true;
                }
            }
        }

        return owners;
    }

    // 沿存档图前进：每个标签段从 owner（含其祖先声明的标签）到 Deep 保存的元素类型及其子类。
    private HashSet<string> Follow(HashSet<string> start, IReadOnlyList<string> segments)
    {
        HashSet<string> current = start;
        foreach (string segment in segments.Where(segment => !Structural.Contains(segment, StringComparer.Ordinal)))
        {
            var next = new HashSet<string>(StringComparer.Ordinal);
            foreach (string owner in current)
            {
                foreach (string declaring in Ancestors(owner).Prepend(owner))
                {
                    foreach (CorpusRow row in graph[declaring].Where(row => segment == "*" || row[1] == segment))
                    {
                        next.Add(row[2]);
                        next.UnionWith(subclasses.GetValueOrDefault(row[2]) ?? []);
                    }
                }
            }

            current = next;
        }

        return current;
    }

    private HashSet<string> Document(IReadOnlyList<string> segments, string? label)
    {
        var owners = new HashSet<string>(StringComparer.Ordinal);
        if (segments.LastOrDefault(segment => !Structural.Contains(segment, StringComparer.Ordinal)) is { } last && last != "*")
        {
            foreach (CorpusRow row in graph.SelectMany(group => group).Where(row => row[1] == last))
            {
                owners.Add(row[2]);
                owners.UnionWith(subclasses.GetValueOrDefault(row[2]) ?? []);
            }
        }

        // 写进原版元素的标签属于补丁所在的静态类（ScribeSchema 找不到 ExposeData owner 时的归属）。
        if (label is not null)
        {
            owners.UnionWith(staticTypes);
        }

        return owners;
    }

    private static int TopLevelComma(string text)
    {
        int depth = 0;
        int last = -1;
        for (int i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    last = i;
                    break;
            }
        }

        return last;
    }

    private static string[] Segments(string path, string selector)
    {
        string[] segments = path.Split('/', StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0))
        {
            throw new FormatException($"'{selector}': empty path segment.");
        }

        return segments;
    }
}
