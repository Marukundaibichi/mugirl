using System.Text.RegularExpressions;

namespace RenameApply;

// Old -> new name tables in every textual form the passes meet, plus the bounded regexes that find them.
public sealed class NameTables
{
    // Full type names: metadata form ("Ns.Outer+Inner"), C# form ("Ns.Outer.Inner", no arity) and IL form ("Ns.Outer/Inner").
    public Dictionary<string, string> TypeFull { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> TypeShort { get; } = new(StringComparer.Ordinal);       // short names that change
    public HashSet<string> DistinctiveShort { get; } = new(StringComparer.Ordinal);           // safe to rewrite in prose
    public List<(string Old, string New, DefRename Def)> DefNames { get; } = new();
    public Dictionary<string, List<(string New, DefRename Def)>> DefByOld { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Keys { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Abstract { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> KeyPrefixes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);  // rel path forms -> new

    public Regex? TypeFullRx { get; private set; }
    public Regex? TypeShortRx { get; private set; }
    public Regex? DistinctiveShortRx { get; private set; }
    public Regex? DefRx { get; private set; }
    public Regex? KeyRx { get; private set; }
    public Regex? AbstractRx { get; private set; }
    public Regex? PathRx { get; private set; }

    public static NameTables Build(RenameModel m, IEnumerable<TypeMapEntry> typeMap, ISet<string> ambiguousShortNames, IEnumerable<(string Old, string New)> movedFiles)
    {
        var t = new NameTables();
        foreach (TypeMapEntry e in typeMap)
        {
            if (e.Old == e.New)
            {
                continue;
            }
            ClrName o = ClrName.Parse(e.Old), n = ClrName.Parse(e.New);
            t.TypeFull[o.Metadata] = n.Metadata;
            t.TypeFull[o.CSharpForm] = n.CSharpForm;
            t.TypeFull[o.Metadata.Replace('+', '/')] = n.Metadata.Replace('+', '/');
            if (!e.Implicit && o.Short != n.Short)
            {
                var pairs = new List<(string O, string N)> { (o.Short, n.Short) };
                const string attr = "Attribute";
                if (o.Short.EndsWith(attr, StringComparison.Ordinal) && o.Short.Length > attr.Length)
                {
                    // Attribute usages and prose write the short form: [MugirlPatch] for MugirlPatchAttribute.
                    pairs.Add((o.Short[..^attr.Length], n.Short.EndsWith(attr, StringComparison.Ordinal) ? n.Short[..^attr.Length] : n.Short));
                }
                foreach (var (os, ns) in pairs)
                {
                    t.TypeShort[os] = ns;
                    if (os.Length >= 6 && !ambiguousShortNames.Contains(os) && (os.Contains('_') || os.Count(char.IsUpper) >= 2))
                    {
                        t.DistinctiveShort.Add(os);
                    }
                }
            }
        }
        foreach (DefRename d in m.Defs)
        {
            foreach (var (o, n) in d.AllNames())
            {
                t.DefNames.Add((o, n, d));
                if (!t.DefByOld.TryGetValue(o, out var list))
                {
                    t.DefByOld[o] = list = new List<(string, DefRename)>();
                }
                list.Add((n, d));
            }
        }
        foreach (var kv in m.Keyed)
        {
            t.Keys[kv.Key] = kv.Value;
            string[] parts = kv.Key.Split('.');
            for (int i = 1; i < parts.Length; i++)
            {
                string prefix = string.Join(".", parts.Take(i)) + ".";
                t.KeyPrefixes[prefix] = t.KeyPrefixes.GetValueOrDefault(prefix) + 1;
            }
        }
        foreach (var kv in m.AbstractNames)
        {
            t.Abstract[kv.Key] = kv.Value;
        }
        t.SetPaths(movedFiles);
        t.TypeFullRx = Alt(t.TypeFull.Keys, @"(?<![\w.`])", @"(?![\w`])");
        t.TypeShortRx = Alt(t.TypeShort.Keys, @"(?<![\w])", @"(?![\w])");
        t.DistinctiveShortRx = Alt(t.DistinctiveShort, @"(?<![\w.])", @"(?![\w])");
        t.DefRx = Alt(t.DefByOld.Keys, @"(?<![\w])", @"(?![\w])");
        t.KeyRx = Alt(t.Keys.Keys, @"(?<![\w.])", @"(?![\w]|\.\w)");
        t.AbstractRx = Alt(t.Abstract.Keys, @"(?<![\w])", @"(?![\w])");
        return t;
    }

    // Moved files (C# and content), in repo-relative form and, for the mod project, Source/MugirlRace-relative form.
    public void SetPaths(IEnumerable<(string Old, string New)> movedFiles)
    {
        foreach (var (o, n) in movedFiles)
        {
            Paths[o] = n;
            const string main = "Source/MugirlRace/";
            if (o.StartsWith(main, StringComparison.OrdinalIgnoreCase) && n.StartsWith(main, StringComparison.OrdinalIgnoreCase))
            {
                Paths[o.Substring(main.Length)] = n.Substring(main.Length);
            }
        }
        PathRx = Alt(Paths.Keys.SelectMany(p => new[] { p, p.Replace('/', '\\') }), @"(?<![\w./\\-])", @"(?![\w.\\/-])", RegexOptions.IgnoreCase);
    }

    public static Regex? Alt(IEnumerable<string> names, string before, string after, RegexOptions extra = RegexOptions.None)
    {
        var list = names.Distinct().OrderByDescending(n => n.Length).ThenBy(n => n, StringComparer.Ordinal).ToList();
        if (list.Count == 0)
        {
            return null;
        }
        return new Regex(before + "(?:" + string.Join("|", list.Select(Regex.Escape)) + ")" + after, RegexOptions.CultureInvariant | extra);
    }

    public string MapPath(string matched)
    {
        bool back = matched.Contains('\\');
        string n = Paths[matched.Replace('\\', '/')];
        return back ? n.Replace('/', '\\') : n;
    }

    // A site listed in some def's notRefs: the old text there means something else (a tag, another def type); never renamed.
    public bool IsNotRef(string old, string refKey)
    {
        if (!DefByOld.TryGetValue(old, out var list))
        {
            return false;
        }
        bool hit = false;
        foreach (var (_, d) in list)
        {
            if (d.NotRefs.Contains(refKey))
            {
                d.UsedNotRefs.Add(refKey);
                hit = true;
            }
        }
        return hit;
    }

    // Single non-shared def target for an old name (null when several defs share it).
    public string? UniqueDefTarget(string old, out DefRename? def)
    {
        def = null;
        if (!DefByOld.TryGetValue(old, out var list))
        {
            return null;
        }
        var targets = list.Select(x => x.New).Distinct().ToList();
        if (targets.Count != 1 || list.Any(x => x.Def.Shared))
        {
            def = list[0].Def;
            return null;
        }
        def = list[0].Def;
        return targets[0];
    }
}
