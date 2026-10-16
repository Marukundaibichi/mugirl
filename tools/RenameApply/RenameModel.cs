using System.Text.Json;
using System.Text.RegularExpressions;

namespace RenameApply;

// A CLR type name split into namespace + nesting chain. Metadata form: "Ns.Outer+Inner`1"; doc-id form: "Ns.Outer.Inner`1".
public sealed record ClrName(string Namespace, IReadOnlyList<string> Chain)
{
    public string Metadata => (Namespace.Length > 0 ? Namespace + "." : "") + string.Join("+", Chain);
    public string DocForm => (Namespace.Length > 0 ? Namespace + "." : "") + string.Join(".", Chain);
    public string CSharpForm => (Namespace.Length > 0 ? Namespace + "." : "") + string.Join(".", Chain.Select(StripArity));
    public string ShortMetadata => Chain[^1];
    public string Short => StripArity(Chain[^1]);
    public bool IsNested => Chain.Count > 1;
    public ClrName Outermost => new(Namespace, new[] { Chain[0] });

    public static string StripArity(string n)
    {
        int i = n.IndexOf('`');
        return i < 0 ? n : n.Substring(0, i);
    }

    private static readonly Regex Valid = new(@"^[A-Za-z_][\w]*(\.[A-Za-z_][\w]*)*(\+[A-Za-z_][\w]*(`\d+)?)*(`\d+)?(\+[A-Za-z_][\w]*(`\d+)?)*$");

    public static ClrName Parse(string s)
    {
        s = s.Trim();
        if (!Valid.IsMatch(s))
        {
            throw new ToolException($"not a CLR type name (use Namespace.Type, Outer+Inner, Type`1): '{s}'");
        }
        string[] plus = s.Split('+');
        string first = plus[0];
        int dot = first.LastIndexOf('.');
        string ns = dot < 0 ? "" : first.Substring(0, dot);
        var chain = new List<string> { dot < 0 ? first : first.Substring(dot + 1) };
        chain.AddRange(plus.Skip(1));
        return new ClrName(ns, chain);
    }
}

public sealed class TypeRename
{
    public required TypeEntry Entry { get; init; }
    public required ClrName Old { get; init; }
    public required ClrName New { get; init; }
    public bool NamespaceChanges => Old.Namespace != New.Namespace;
    public bool ShortChanges => Old.ShortMetadata != New.ShortMetadata;
}

public sealed class DefRename
{
    public required DefEntry Entry { get; init; }
    public string Old => Entry.Old;
    public string New => Entry.New;
    public string DefType => Entry.DefType;
    public bool Shared => Entry.SharedNameWithOtherDefTypes;
    public HashSet<string> ExplicitRefs { get; } = new(StringComparer.OrdinalIgnoreCase); // "rel/path.xml:LINE"
    public HashSet<string> UsedExplicitRefs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> NotRefs { get; } = new(StringComparer.OrdinalIgnoreCase);     // "rel/path:LINE": never this def
    public HashSet<string> UsedNotRefs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Root { get; set; }                     // resolved root Def type full name
    public bool IsThingDef => Root == "Verse.ThingDef";

    // Names the engine derives from a ThingDef's defName (design.md 5.4): rename where they appear.
    public static readonly string[] DerivedPrefixes = { "Blueprint_Install_", "Blueprint_", "Frame_", "Corpse_", "Meat_", "Make_", "Administer_" };

    // Database of a derived name: recipes for Make_/Administer_, things for the rest.
    public static string DerivedRoot(string name) =>
        name.StartsWith("Make_", StringComparison.Ordinal) || name.StartsWith("Administer_", StringComparison.Ordinal) ? "Verse.RecipeDef" : "Verse.ThingDef";

    public IEnumerable<(string Old, string New)> AllNames()
    {
        yield return (Old, New);
        if (IsThingDef)
        {
            foreach (string p in DerivedPrefixes)
            {
                yield return (p + Old, p + New);
            }
        }
    }
}

public sealed class FrozenNames
{
    public List<string> DefNames { get; set; } = new();          // exact names or globs ('*' anywhere)
    public List<string> AbstractNames { get; set; } = new();
    public List<string> TypeShortNames { get; set; } = new();
    public List<string> FrozenTypes { get; set; } = new();       // full metadata names that keep their name (PublicApi)
    public List<string> TypesAliasRequired { get; set; } = new();
    public string Source { get; set; } = "";

    // Accepts the frozen-names format ({defNames, abstractNames, typeShortNames, frozenTypes, typesAliasRequired}) or
    // spec/final/public-api.json ({entries: [{kind, name, treatment}]}), which is the authoritative input (decisions 5).
    public static FrozenNames Load(string? path)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "frozen-names.json");
        if (!File.Exists(path))
        {
            throw new ToolException($"frozen names not found: {path} (pass --frozen-names spec/final/public-api.json)");
        }
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!doc.RootElement.TryGetProperty("entries", out JsonElement entries))
        {
            FrozenNames f = doc.RootElement.Deserialize<FrozenNames>(MergedSpec.JsonOptions) ?? new FrozenNames();
            f.Source = path;
            return f;
        }
        var r = new FrozenNames { Source = path };
        foreach (JsonElement e in entries.EnumerateArray())
        {
            string kind = e.GetProperty("kind").GetString() ?? "";
            string name = e.GetProperty("name").GetString() ?? "";
            string treatment = e.TryGetProperty("treatment", out JsonElement t) ? t.GetString() ?? "" : "";
            switch (kind)
            {
                case "defName":
                case "defNamePattern":
                    r.DefNames.Add(name);
                    break;
                case "abstractName":
                    r.AbstractNames.Add(name);
                    break;
                case "typeShortName":
                    r.TypeShortNames.Add(name);
                    break;
                case "type":
                case "xmlClass":
                    if (treatment.Contains("alias subclass", StringComparison.Ordinal))
                    {
                        r.TypesAliasRequired.Add(name);
                    }
                    else
                    {
                        r.FrozenTypes.Add(name);
                    }
                    break;
            }
        }
        return r;
    }

    public static bool Matches(IEnumerable<string> patterns, string name) =>
        patterns.Any(p => p.Contains('*')
            ? Regex.IsMatch(name, "^" + string.Join(".*", p.Split('*').Select(Regex.Escape)) + "$", RegexOptions.CultureInvariant)
            : p == name);
}

// Everything the passes need, derived once from the merged spec. Pure spec checks happen here; repo checks in the passes.
public sealed class RenameModel
{
    public List<TypeRename> Types { get; } = new();
    public Dictionary<string, TypeRename> TypesByOldMetadata { get; } = new(StringComparer.Ordinal);
    public List<DefRename> Defs { get; } = new();
    public Dictionary<string, string> Keyed { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> AbstractNames { get; } = new(StringComparer.Ordinal);
    public List<(string Old, string New, string Module)> SourceFileMoves { get; } = new();   // repo-relative
    public List<(string Old, string New, string Module, bool Auto)> ContentFileMoves { get; } = new();
    public Dictionary<string, string> KeyedModule { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> AbstractModule { get; } = new(StringComparer.Ordinal);
    public MergedSpec Spec { get; private set; } = new();

    // Built after implicit nested types are known (C# pass): every old full name (doc form) -> new full name (doc form).
    public Dictionary<string, string> DocNameMap { get; } = new(StringComparer.Ordinal);
    private Regex? _docNameRegex;

    public static RenameModel Build(MergedSpec spec, FrozenNames frozen, RunReport report)
    {
        var m = new RenameModel { Spec = spec };
        // Types
        foreach (var g in spec.Types.GroupBy(t => t.Old.Trim(), StringComparer.Ordinal))
        {
            var distinct = g.Select(t => (t.New.Trim(), RepoPaths.Norm(t.NewFile ?? ""), RepoPaths.Norm(t.OldFile ?? ""))).Distinct().ToList();
            if (distinct.Count > 1)
            {
                report.Error("spec-conflict", $"type {g.Key} has conflicting entries: {string.Join(" | ", g.Select(t => $"{t.Module}: {t.New} {t.NewFile}"))}");
                continue;
            }
            TypeEntry e = g.First();
            ClrName oldName, newName;
            try
            {
                oldName = ClrName.Parse(e.Old);
                newName = ClrName.Parse(e.New);
            }
            catch (ToolException ex)
            {
                report.Error("spec-invalid", ex.Message);
                continue;
            }
            if (oldName.Chain.Count != newName.Chain.Count)
            {
                report.Error("spec-invalid", $"type {e.Old} -> {e.New}: nesting depth differs");
                continue;
            }
            if (oldName.Metadata == newName.Metadata && (e.NewFile == null || RepoPaths.Norm(e.NewFile) == RepoPaths.Norm(e.OldFile ?? "")))
            {
                report.Warn("spec-noop", $"type {e.Old}: old == new and no file change", null);
                continue;
            }
            if (frozen.FrozenTypes.Contains(oldName.Metadata) && oldName.Metadata != newName.Metadata)
            {
                report.Error("spec-frozen", $"type {e.Old} is frozen public API ({frozen.Source}); it may move files but keeps its name");
            }
            if (FrozenNames.Matches(frozen.TypeShortNames, oldName.Short) && oldName.Short != newName.Short)
            {
                report.Error("spec-frozen", $"type {e.Old}: short name '{oldName.Short}' is public API (design.md section 2)");
            }
            if (frozen.TypesAliasRequired.Contains(oldName.Metadata) && oldName.Metadata != newName.Metadata)
            {
                report.Warn("public-alias-needed", $"type {oldName.Metadata} -> {newName.Metadata} is referenced by other mods: LegacyTypes must alias the old name for XML (design.md 5.2.4)");
            }
            if ((newName.Namespace == "Mugirl" || newName.Namespace.Length == 0) && oldName.Namespace != newName.Namespace
                && !(e.NewFile != null && RepoPaths.Norm(e.NewFile).StartsWith("Source/MugirlRace/PublicApi/", StringComparison.Ordinal)))
            {
                report.Error("spec-root-namespace", $"type {e.New}: target is the root namespace (design.md 3.1 forbids it)");
            }
            foreach (string seg in newName.Namespace.Split('.'))
            {
                if (ForbiddenNamespaceSegments.Contains(seg))
                {
                    report.Error("spec-namespace-shadow", $"type {e.New}: namespace segment '{seg}' shadows an engine name (design.md 3.1)");
                }
            }
            var tr = new TypeRename { Entry = e, Old = oldName, New = newName };
            m.Types.Add(tr);
            m.TypesByOldMetadata[oldName.Metadata] = tr;
        }
        foreach (TypeRename t in m.Types)
        {
            if (t.Old.IsNested)
            {
                // A nested rename keeps its outer type's (possibly renamed) path.
                ClrName outerOld = new(t.Old.Namespace, t.Old.Chain.Take(t.Old.Chain.Count - 1).ToList());
                string expectedOuterNew = m.TypesByOldMetadata.TryGetValue(outerOld.Metadata, out TypeRename? o) ? o.New.Metadata : outerOld.Metadata;
                ClrName outerNew = new(t.New.Namespace, t.New.Chain.Take(t.New.Chain.Count - 1).ToList());
                if (outerNew.Metadata != expectedOuterNew)
                {
                    report.Error("spec-invalid", $"nested type {t.Old.Metadata} -> {t.New.Metadata}: outer path must be '{expectedOuterNew}'");
                }
            }
        }
        var newNames = m.Types.GroupBy(t => t.New.Metadata).Where(g => g.Count() > 1);
        foreach (var g in newNames)
        {
            report.Error("spec-conflict", $"several types map to {g.Key}: {string.Join(", ", g.Select(t => t.Old.Metadata))}");
        }
        foreach (TypeRename t in m.Types)
        {
            if (m.TypesByOldMetadata.ContainsKey(t.New.Metadata) && t.New.Metadata != t.Old.Metadata)
            {
                report.Error("spec-chain", $"type chain/swap: {t.Old.Metadata} -> {t.New.Metadata}, which is itself renamed");
            }
        }

        // Defs
        foreach (var g in spec.Defs.GroupBy(d => (DefTypeKey(d.DefType), d.Old.Trim())))
        {
            var distinct = g.Select(d => d.New.Trim()).Distinct().ToList();
            if (distinct.Count > 1)
            {
                report.Error("spec-conflict", $"def {g.Key.Item1} {g.Key.Item2} has conflicting targets: {string.Join(", ", distinct)}");
                continue;
            }
            DefEntry e = g.First();
            e.Old = e.Old.Trim();
            e.New = e.New.Trim();
            if (!Regex.IsMatch(e.New, @"^[A-Za-z_][\w\-]*$"))
            {
                report.Error("spec-invalid", $"def {e.DefType} {e.Old}: invalid new defName '{e.New}'");
                continue;
            }
            if (FrozenNames.Matches(frozen.DefNames, e.Old))
            {
                report.Error("spec-frozen", $"def {e.DefType} {e.Old} is public API (design.md section 2)");
            }
            var dr = new DefRename { Entry = e };
            foreach (DefEntry d in g)
            {
                foreach (string r in d.ExplicitRefs ?? new())
                {
                    dr.ExplicitRefs.Add(NormalizeRef(r));
                }
                foreach (string r in d.NotRefs ?? new())
                {
                    dr.NotRefs.Add(NormalizeRef(r));
                }
            }
            if (e.SharedNameWithOtherDefTypes && dr.ExplicitRefs.Count == 0)
            {
                report.Warn("spec-shared-no-refs", $"def {e.DefType} {e.Old} is flagged shared but lists no explicitRefs: only its <defName>, DefOf fields and DefInjected keys are renamed");
            }
            m.Defs.Add(dr);
        }
        foreach (var g in m.Defs.GroupBy(d => (DefTypeKey(d.DefType), d.New)).Where(g => g.Count() > 1))
        {
            report.Error("spec-conflict", $"several {g.Key.Item1} defs map to {g.Key.New}: {string.Join(", ", g.Select(d => d.Old))}");
        }

        AddNameMap(spec.Keyed, m.Keyed, m.KeyedModule, "keyed", null, report);
        AddNameMap(spec.AbstractNames, m.AbstractNames, m.AbstractModule, "abstract Name", frozen.AbstractNames, report);

        // Moves
        foreach (var g in spec.FileMoves.GroupBy(f => RepoPaths.Norm(f.Old), StringComparer.OrdinalIgnoreCase))
        {
            var targets = g.Select(f => RepoPaths.Norm(f.New)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (targets.Count > 1)
            {
                report.Error("spec-conflict", $"file {g.Key} moves to several targets: {string.Join(", ", targets)}");
                continue;
            }
            m.SourceFileMoves.Add((g.Key, targets[0], g.First().Module));
        }
        foreach (var g in spec.ContentFileMoves.GroupBy(f => RepoPaths.Norm(f.Old), StringComparer.OrdinalIgnoreCase))
        {
            var targets = g.Select(f => RepoPaths.Norm(f.New)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (targets.Count > 1)
            {
                report.Error("spec-conflict", $"content file {g.Key} moves to several targets: {string.Join(", ", targets)}");
                continue;
            }
            m.ContentFileMoves.Add((g.Key, targets[0], g.First().Module, false));
        }
        foreach (var (o, n, _) in m.SourceFileMoves)
        {
            CheckPath(o, "fileMoves.old", report, mustBeEditable: true);
            CheckPath(n, "fileMoves.new", report, mustBeEditable: true);
        }
        foreach (var (o, n, _, _) in m.ContentFileMoves)
        {
            CheckPath(o, "contentFileMoves.old", report, mustBeEditable: true);
            CheckPath(n, "contentFileMoves.new", report, mustBeEditable: true);
        }
        foreach (TypeRename t in m.Types)
        {
            if (t.Entry.OldFile != null)
            {
                CheckPath(RepoPaths.Norm(t.Entry.OldFile), $"types[{t.Old.Metadata}].oldFile", report, mustBeEditable: true);
            }
            if (t.Entry.NewFile != null)
            {
                CheckPath(RepoPaths.Norm(t.Entry.NewFile), $"types[{t.Old.Metadata}].newFile", report, mustBeEditable: true);
            }
        }
        return m;
    }

    private static readonly HashSet<string> ForbiddenNamespaceSegments = new(StringComparer.Ordinal)
    {
        "UI", "Text", "Log", "Find", "Gen", "Widgets", "Scribe", "AlienRace", "HarmonyLib", "RimWorld", "Verse", "LudeonTK", "System",
        "UnityEngine", "Steamworks", "Apparel",
    };

    private static void CheckPath(string rel, string what, RunReport report, bool mustBeEditable)
    {
        if (rel.Contains("..") || Path.IsPathRooted(rel))
        {
            report.Error("spec-path", $"{what}: '{rel}' must be repo-relative");
        }
        else if (mustBeEditable && !RepoPaths.IsEditable(rel))
        {
            report.Error("spec-path", $"{what}: '{rel}' is outside the editable zones (Source/, 1.7/, tools/*.json) or frozen");
        }
    }

    private static void AddNameMap(List<RenameEntry> entries, Dictionary<string, string> map, Dictionary<string, string> moduleMap, string what, List<string>? frozen, RunReport report)
    {
        foreach (var g in entries.GroupBy(k => k.Old.Trim(), StringComparer.Ordinal))
        {
            var targets = g.Select(k => k.New.Trim()).Distinct(StringComparer.Ordinal).ToList();
            if (targets.Count > 1)
            {
                report.Error("spec-conflict", $"{what} {g.Key} has conflicting targets: {string.Join(", ", targets)}");
                continue;
            }
            if (frozen != null && FrozenNames.Matches(frozen, g.Key))
            {
                report.Error("spec-frozen", $"{what} {g.Key} is public API (design.md section 2)");
            }
            if (!Regex.IsMatch(targets[0], @"^[A-Za-z_][\w.\-]*$"))
            {
                report.Error("spec-invalid", $"{what} {g.Key}: invalid new name '{targets[0]}'");
                continue;
            }
            map[g.Key] = targets[0];
            moduleMap[g.Key] = g.First().Module;
        }
        foreach (var kv in map)
        {
            if (map.ContainsKey(kv.Value))
            {
                report.Error("spec-chain", $"{what} chain/swap: {kv.Key} -> {kv.Value}, which is itself renamed");
            }
        }
        foreach (var g in map.GroupBy(kv => kv.Value).Where(g => g.Count() > 1))
        {
            report.Error("spec-conflict", $"several {what}s map to {g.Key}: {string.Join(", ", g.Select(kv => kv.Key))}");
        }
    }

    public static string DefTypeKey(string defType) => defType.Trim();

    public static string NormalizeRef(string r)
    {
        r = RepoPaths.Norm(r.Trim());
        int colon = r.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(r.AsSpan(colon + 1), out int line))
        {
            return r;
        }
        return r.Substring(0, colon) + ":" + line;
    }

    // ---- doc-id / CLR name mapping ------------------------------------------------------------------------------------

    public void AddDocName(string oldDoc, string newDoc)
    {
        DocNameMap[oldDoc] = newDoc;
        _docNameRegex = null;
        if (oldDoc.Contains('`'))
        {
            // Constructed generic types appear without arity in doc ids ("Ns.Cache{System.Int32}") and messages ("Ns.Cache<int>").
            GenericStripped[Regex.Replace(oldDoc, "`\\d+", "")] = Regex.Replace(newDoc, "`\\d+", "");
            _genericRegex = null;
        }
    }

    private Dictionary<string, string> GenericStripped { get; } = new(StringComparer.Ordinal);
    private Regex? _genericRegex;

    private string MapGeneric(string s)
    {
        if (GenericStripped.Count == 0)
        {
            return s;
        }
        _genericRegex ??= new Regex(@"(?<![\w.`])(?:" + string.Join("|", GenericStripped.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + @")(?=[{<])", RegexOptions.CultureInvariant);
        return _genericRegex.Replace(s, mt => GenericStripped[mt.Value]);
    }

    // Replaces every old type name (doc form, i.e. nested types separated by '.') inside a documentation-comment id or
    // display string; longest names first so explicitly renamed nested types win over their outer type.
    public string MapDocString(string s)
    {
        if (DocNameMap.Count == 0)
        {
            return s;
        }
        _docNameRegex ??= BuildDocRegex(DocNameMap.Keys);
        return MapGeneric(_docNameRegex.Replace(s, mt => DocNameMap[mt.Value]));
    }

    private readonly Dictionary<string, Regex?> _excludingRegex = new(StringComparer.Ordinal);

    // Same mapping for a compilation in which some old names denote a different (foreign) type, e.g. a negative-control
    // project that declares its own Mugirl.Mugirl_DefOf: those names must not be mapped there.
    public string MapDocString(string s, IReadOnlySet<string> exclude)
    {
        if (exclude.Count == 0)
        {
            return MapDocString(s);
        }
        string key = string.Join("|", exclude.OrderBy(x => x, StringComparer.Ordinal));
        if (!_excludingRegex.TryGetValue(key, out Regex? rx))
        {
            var names = DocNameMap.Keys.Where(k => !exclude.Contains(k)).ToList();
            rx = names.Count == 0 ? null : BuildDocRegex(names);
            _excludingRegex[key] = rx;
        }
        return MapGeneric(rx == null ? s : rx.Replace(s, mt => DocNameMap[mt.Value]));
    }

    private static Regex BuildDocRegex(IEnumerable<string> names) =>
        new(@"(?<![\w.`])(?:" + string.Join("|", names.OrderByDescending(k => k.Length).ThenBy(k => k, StringComparer.Ordinal).Select(Regex.Escape)) + @")(?![\w`])", RegexOptions.CultureInvariant);
}
