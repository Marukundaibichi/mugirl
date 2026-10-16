using System.Text.Json;
using Mono.Cecil;

namespace MugirlRace.Validation.Parity;

/// <summary>
/// 一致性比较用的改名映射：RenameApply 的应用记录（顶层或 <c>maps</c> 下的 <c>types/defs/members/defOfFields/keyed/abstractNames/files</c>）
/// 或 TSV（<c>kind&lt;TAB&gt;old&lt;TAB&gt;new</c>）。A 侧的名字经它映射后与 B 侧比较。
/// </summary>
/// <remarks>
/// <para>
/// 成员（<c>members</c>）可以改名，也可以移到另一个类型（<c>move</c>，例如 DefOf 拆分）、复制（<c>copy</c>）、删除（<c>delete</c>），
/// 类型可以整体解散（<c>dissolve</c>，成员都移走）。成员键是 <c>Owner.Name</c>，方法可以带参数表 <c>Owner.Name(T1,T2@)</c>（文档 id 写法）；
/// 不带参数表的方法键适用于全部重载。
/// </para>
/// <para>
/// TSV 的 kind：type、member（改名或移动，new 为完整的 <c>NewOwner.NewName</c>）、copy、delete（new 留空或 -）、dissolve（类型）、
/// def（old 可写成 <c>Root/old</c>）、key、abstractName、file。
/// </para>
/// </remarks>
internal sealed class RenameMap
{
    private readonly Dictionary<string, string> types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Owner, string Name)> members = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Owner, string Name)>> copies = new(StringComparer.Ordinal);
    private readonly HashSet<string> deletedMembers = new(StringComparer.Ordinal);
    private readonly HashSet<string> dissolvedTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Root, string Name), string> defsByRoot = [];
    private readonly Dictionary<string, string?> defsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> abstractNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);
    private Dictionary<string, string?>? shortTypes;

    public int Count => types.Count + members.Count + copies.Values.Sum(c => c.Count) + deletedMembers.Count + dissolvedTypes.Count + defsByRoot.Count + keys.Count + abstractNames.Count + files.Count;

    /// <summary>移动或复制成员的目标（B 侧 <c>Owner.Name</c>）：B 中这些成员不是新增。</summary>
    public IEnumerable<(string Owner, string Name)> MemberTargets => members
        .Where(pair => pair.Value.Owner != Type(MemberOwner(pair.Key)))
        .Select(pair => pair.Value).Concat(copies.Values.SelectMany(c => c));

    public static RenameMap Load(string? path)
    {
        var map = new RenameMap();
        if (path is null)
        {
            return map;
        }

        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            using JsonDocument document = JsonDocument.Parse(System.IO.File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            JsonElement root = document.RootElement;
            JsonElement maps = Property(root, "maps") ?? root;
            foreach ((string old, string current, JsonElement _) in Entries(maps, "types"))
            {
                map.types[old] = current;
            }

            foreach ((string old, string current, JsonElement entry) in Entries(maps, "defs"))
            {
                map.AddDef(Text(entry, "defType") ?? Text(entry, "root"), old, current);
            }

            foreach ((string old, string current, JsonElement _) in Entries(maps, "defOfFields"))
            {
                map.AddMember("rename", old, current);
            }

            foreach ((string old, string current, JsonElement entry) in Entries(maps, "members", allowEmptyNew: true))
            {
                map.AddMember(Text(entry, "kind") ?? "rename", old, current);
            }

            foreach ((string old, string current, JsonElement _) in Entries(maps, "keyed"))
            {
                map.keys[old] = current;
            }

            foreach ((string old, string current, JsonElement _) in Entries(maps, "abstractNames"))
            {
                map.abstractNames[old] = current;
            }

            foreach ((string old, string current, JsonElement _) in Entries(maps, "files"))
            {
                map.files[old] = current;
            }

            return map;
        }

        foreach (string line in System.IO.File.ReadLines(path).Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && !l.StartsWith('#')))
        {
            string[] parts = line.Split('\t');
            if (parts.Length < 2 || (parts.Length < 3 && parts[0] is not ("delete" or "dissolve")))
            {
                throw new Infrastructure.UsageException($"{path}: '{line}' needs kind<TAB>old<TAB>new.");
            }

            string current = parts.Length > 2 ? parts[2] : "";
            switch (parts[0])
            {
                case "type":
                    map.types[parts[1]] = current;
                    break;
                case "member" or "move" or "rename" or "copy" or "delete" or "dissolve":
                    map.AddMember(parts[0] == "member" ? "rename" : parts[0], parts[1], current);
                    break;
                case "def":
                    int slash = parts[1].IndexOf('/', StringComparison.Ordinal);
                    map.AddDef(slash > 0 ? parts[1][..slash] : null, slash > 0 ? parts[1][(slash + 1)..] : parts[1], current);
                    break;
                case "key":
                    map.keys[parts[1]] = current;
                    break;
                case "abstractName":
                    map.abstractNames[parts[1]] = current;
                    break;
                case "file":
                    map.files[parts[1]] = current;
                    break;
                default:
                    throw new Infrastructure.UsageException($"{path}: unknown kind '{parts[0]}'.");
            }
        }

        return map;
    }

    /// <summary>A 侧完整类型名（嵌套用 <c>+</c>）在 B 侧的名字；嵌套类型跟随外层类型。</summary>
    public string Type(string name)
    {
        if (types.TryGetValue(name, out string? mapped))
        {
            return mapped;
        }

        int plus = name.LastIndexOf('+');
        return plus > 0 ? Type(name[..plus]) + name[plus..] : name;
    }

    /// <summary>A 侧类型是否整体解散（成员移到别处，B 中没有对应类型）。</summary>
    public bool IsDissolved(string owner) => dissolvedTypes.Contains(owner);

    /// <summary>
    /// A 侧成员 <c>Owner.Name</c>（<paramref name="parameters"/> 是方法的文档 id 参数表，字段为 <see langword="null"/>）在 B 侧的位置：
    /// 移动时为新的所属类型与名字，改名时所属类型为 <see langword="null"/>（跟随类型映射），删除时 <c>Deleted</c> 为 true。
    /// </summary>
    public (string? Owner, string Name, bool Deleted, bool Copied) Member(string owner, string name, string? parameters = null)
    {
        string key = owner + "." + name;
        string? exact = parameters is null ? null : key + "(" + parameters + ")";
        if ((exact is not null && deletedMembers.Contains(exact)) || deletedMembers.Contains(key))
        {
            return (null, name, true, false);
        }

        string? found = exact is not null && members.ContainsKey(exact) ? exact : members.ContainsKey(key) ? key : null;
        if (found is null)
        {
            return (null, name, false, copies.ContainsKey(key));
        }

        (string newOwner, string newName) = members[found];
        bool moved = newOwner != owner && newOwner != Type(owner);
        return (moved ? newOwner : null, newName, false, copies.ContainsKey(found));
    }

    /// <summary>每个复制目标都必须单独比较，不能让后一项覆盖前一项。</summary>
    public IReadOnlyList<(string Owner, string Name)> CopiesOf(string owner, string name)
        => copies.GetValueOrDefault(owner + "." + name) ?? [];

    /// <summary>规范化已显式声明的字段复制引用；目标的形状和绑定仍由逐目标比较验证。</summary>
    public string? CopiedFieldIdentity(string owner, string name)
    {
        string key = owner + "." + name;
        if (copies.ContainsKey(key)) { return "copy:" + key; }
        foreach ((string source, List<(string Owner, string Name)> targets) in copies)
        {
            if (targets.Contains((owner, name))) { return "copy:" + source; }
        }

        return null;
    }

    /// <summary>规格按属性符号移动；IL 中是其访问器。用双方真实属性元数据展开，不猜成员名字。</summary>
    public void BindAccessors(ModuleDefinition a, ModuleDefinition b)
    {
        foreach ((string source, (string owner, string name)) in members.ToArray())
        {
            string oldOwner = MemberOwner(source);
            TypeDefinition? oldType = a.GetType(oldOwner.Replace('+', '/'));
            TypeDefinition? newType = b.GetType(owner.Replace('+', '/'));
            PropertyDefinition? oldProperty = oldType?.Properties.FirstOrDefault(p => p.Name == MemberShort(source));
            PropertyDefinition? newProperty = newType?.Properties.FirstOrDefault(p => p.Name == name);
            if (oldProperty is null || newProperty is null) { continue; }
            if (oldProperty.GetMethod is { } oldGet && newProperty.GetMethod is { } newGet) { AddMember("move", oldOwner + "." + oldGet.Name, owner + "." + newGet.Name); }
            if (oldProperty.SetMethod is { } oldSet && newProperty.SetMethod is { } newSet) { AddMember("move", oldOwner + "." + oldSet.Name, owner + "." + newSet.Name); }
        }
    }

    /// <summary>A 侧成员改名后的名字（所属类型不变时）；兼容只关心名字的调用方。</summary>
    public string MemberName(string owner, string name) => Member(owner, name).Name;

    /// <summary>defName 在 B 侧的名字。<paramref name="root"/> 已知时按数据库根，否则只在各根映射一致时改名。</summary>
    public string Def(string name, string? root = null)
    {
        if (root is not null)
        {
            string shortRoot = root[(root.LastIndexOf('.') + 1)..];
            return defsByRoot.GetValueOrDefault((shortRoot, name), name);
        }

        return defsByName.TryGetValue(name, out string? mapped) && mapped is not null ? mapped : name;
    }

    public string Key(string name) => keys.GetValueOrDefault(name, name);

    public string AbstractName(string name) => abstractNames.GetValueOrDefault(name, name);

    public string File(string path) => files.GetValueOrDefault(path, path);

    public bool IsMappedDef(string name) => defsByName.ContainsKey(name);

    /// <summary>短类型名（DefInjected 句柄、XML 中的忽略命名空间写法）的映射；短名不唯一时不映射。</summary>
    public string ShortType(string shortName)
    {
        if (shortTypes is null)
        {
            shortTypes = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach ((string from, string to) in types)
            {
                string key = Short(from);
                string value = Short(to);
                shortTypes[key] = shortTypes.TryGetValue(key, out string? existing) && existing != value ? null : value;
            }
        }

        return shortTypes.TryGetValue(shortName, out string? mapped) && mapped is not null ? mapped : shortName;
    }

    /// <summary>
    /// 字符串字面量的变化能否由映射解释：旧值是某个改名的完整类型名、短类型名、defName、Keyed 键、抽象 Name 或成员名，且新值就是它的映射结果。
    /// </summary>
    public bool Explains(string old, string current)
    {
        if (types.TryGetValue(old, out string? type) && type == current)
        {
            return true;
        }

        if (ShortType(old) == current && old != current)
        {
            return true;
        }

        return Def(old) == current && old != current || defsByRoot.Any(pair => pair.Key.Name == old && pair.Value == current)
            || keys.GetValueOrDefault(old) == current || abstractNames.GetValueOrDefault(old) == current
            || members.Any(pair => MemberShort(pair.Key) == old && pair.Value.Name == current);
    }

    private static string Short(string type) => type[(Math.Max(type.LastIndexOf('.'), type.LastIndexOf('+')) + 1)..];

    // Owner.Name(参数) 的 Name 部分。
    private static string MemberOwner(string key)
    {
        int open = key.IndexOf('(', StringComparison.Ordinal);
        string head = open < 0 ? key : key[..open];
        return head[..head.LastIndexOf('.')];
    }

    private static string MemberShort(string key)
    {
        int open = key.IndexOf('(', StringComparison.Ordinal);
        string head = open < 0 ? key : key[..open];
        return head[(head.LastIndexOf('.') + 1)..];
    }

    private void AddMember(string kind, string old, string current)
    {
        switch (kind)
        {
            case "delete":
                deletedMembers.Add(old);
                return;
            case "dissolve":
                dissolvedTypes.Add(old);
                return;
        }

        if (current.Length == 0 || current == "-")
        {
            return;
        }

        // 新名的所属类型与名字：参数表中的点不算分隔符。
        int open = current.IndexOf('(', StringComparison.Ordinal);
        string head = open < 0 ? current : current[..open];
        int dot = head.LastIndexOf('.');
        (string Owner, string Name) target = (head[..dot], head[(dot + 1)..]);
        if (kind == "copy")
        {
            if (!copies.TryGetValue(old, out List<(string Owner, string Name)>? targets)) { copies[old] = targets = []; }
            if (!targets.Contains(target)) { targets.Add(target); }
        }
        else { members[old] = target; }
    }

    private void AddDef(string? root, string old, string current)
    {
        string? shortRoot = root is null ? null : root[(root.LastIndexOf('.') + 1)..];
        if (shortRoot is not null)
        {
            defsByRoot[(shortRoot, old)] = current;
        }

        defsByName[old] = defsByName.TryGetValue(old, out string? existing) && existing != current ? null : current;
    }

    private static IEnumerable<(string Old, string New, JsonElement Entry)> Entries(JsonElement maps, string property, bool allowEmptyNew = false)
    {
        if (Property(maps, property) is not { ValueKind: JsonValueKind.Array } array)
        {
            yield break;
        }

        foreach (JsonElement entry in array.EnumerateArray())
        {
            if (Text(entry, "old") is { Length: > 0 } old && Text(entry, "new") is { } current && (allowEmptyNew || current.Length > 0))
            {
                yield return (old, current, entry);
            }
        }
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? Text(JsonElement element, string name) => Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}
