using System.Text.RegularExpressions;
using System.Xml;

namespace RenameApply.Xml;

// Text == null: delete OutFull (a file emptied by the run).
public sealed record PendingWrite(string OutFull, string? Text, bool Bom, string? DeleteFull);

// XML part of Wave R over 1.7/** (Defs, Patches, Languages/*/{Keyed,DefInjected,Strings}, Mods/**): span edits only.
public sealed class XmlPass
{
    private enum Kind { Defs, Patches, Keyed, DefInjected, Strings, Other }

    private sealed class XFile
    {
        public required string Rel { get; init; }
        public required string Full { get; init; }
        public required Kind Kind { get; init; }
        public required string Root { get; init; }          // "1.7" or "1.7/Mods/<X>"
        public string? Lang { get; init; }
        public string? DefInjectedFolder { get; init; }
        public TextFileData Data { get; set; } = null!;
        public XDoc? Doc { get; set; }
        public LineIndex Lines { get; set; } = null!;
        public List<TextEdit> Edits { get; } = new();
        public string OutRel { get; set; } = "";
    }

    private readonly RepoPaths _repo;
    private readonly RenameModel _m;
    private readonly NameTables _t;
    private readonly DefTypeResolver _types;
    private readonly RunReport _report;
    private readonly List<XFile> _files = new();
    private readonly XmlSchema? _schema;

    public List<PendingWrite> Writes { get; } = new();
    public List<(string Old, string New)> Moves { get; } = new();

    private readonly List<(string Owner, string Old, string New)> _fieldRenames;

    public XmlPass(RepoPaths repo, RenameModel m, NameTables t, DefTypeResolver types, RunReport report, XmlSchema? schema = null, List<(string Owner, string Old, string New)>? fieldRenames = null)
    {
        _schema = schema;
        _fieldRenames = fieldRenames ?? new();
        _repo = repo;
        _m = m;
        _t = t;
        _types = types;
        _report = report;
    }

    private static readonly Regex RootRx = new(@"^(1\.7(?:/Mods/[^/]+)?)/(.*)$", RegexOptions.CultureInvariant);

    public void Run()
    {
        Discover();
        ValidateNames();
        PlanMoves();
        foreach (XFile f in _files)
        {
            if (f.Doc == null)
            {
                continue;
            }
            switch (f.Kind)
            {
                case Kind.Defs:
                case Kind.Patches:
                case Kind.Other:
                    ElementRules(f);
                    CommentRules(f, edit: true);
                    break;
                case Kind.DefInjected:
                    DefInjectedRules(f);
                    CommentRules(f, edit: false);
                    break;
                case Kind.Keyed:
                    KeyedRules(f);
                    CommentRules(f, edit: false);
                    break;
            }
        }
        foreach (XFile f in _files.Where(f => f.Kind == Kind.Strings))
        {
            ReportMentions(f, f.Data.Text, 0, "strings-mentions");
        }
        Finish();
    }

    // ------------------------------------------------------------------------------------------------------------------

    private void Discover()
    {
        string root = _repo.Full("1.7");
        if (!Directory.Exists(root))
        {
            throw new ToolException("1.7/ not found");
        }
        foreach (string full in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            string rel = _repo.Rel(full);
            string ext = Path.GetExtension(full).ToLowerInvariant();
            if (ext != ".xml" && ext != ".txt")
            {
                continue;
            }
            Match m = RootRx.Match(rel);
            if (!m.Success)
            {
                continue;
            }
            string contentRoot = m.Groups[1].Value;
            string[] seg = m.Groups[2].Value.Split('/');
            Kind kind = Kind.Other;
            string? lang = null, folder = null;
            if (seg[0] == "Defs")
            {
                kind = Kind.Defs;
            }
            else if (seg[0] == "Patches")
            {
                kind = Kind.Patches;
            }
            else if (seg[0] == "Languages" && seg.Length >= 4)
            {
                lang = seg[1];
                kind = seg[2] switch
                {
                    "Keyed" => Kind.Keyed,
                    "DefInjected" => Kind.DefInjected,
                    "Strings" => Kind.Strings,
                    _ => Kind.Other,
                };
                if (kind == Kind.DefInjected && seg.Length >= 5)
                {
                    folder = seg[3];
                }
            }
            if (ext == ".txt" && kind != Kind.Strings)
            {
                continue;
            }
            var f = new XFile { Rel = rel, Full = full, Kind = kind, Root = contentRoot, Lang = lang, DefInjectedFolder = folder, OutRel = rel };
            f.Data = TextFileData.Read(full);
            f.Lines = new LineIndex(f.Data.Text);
            if (ext == ".xml")
            {
                try
                {
                    f.Doc = XDoc.Parse(f.Data.Text);
                }
                catch (FormatException ex)
                {
                    _report.Error("xml-parse", ex.Message, rel);
                }
            }
            _files.Add(f);
        }
    }

    private int LineOf(XFile f, int pos) => f.Lines.Get(pos).Line;

    private void Edit(XFile f, int start, int length, string newText, string kind)
    {
        f.Edits.Add(new TextEdit(start, length, newText, kind));
    }

    private static string ShortName(string typeName)
    {
        int i = typeName.LastIndexOf('.');
        return i < 0 ? typeName : typeName.Substring(i + 1);
    }

    private bool RootMatches(DefRename d, string elementType)
    {
        string root = _types.RootOrName(elementType);
        if (d.Root != null)
        {
            return d.Root == root;
        }
        return d.DefType == elementType || ShortName(d.DefType) == ShortName(elementType);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Validation against the content
    // ------------------------------------------------------------------------------------------------------------------

    private IEnumerable<(XFile File, XElem Def, string Type, string Name, int Pos)> DefNameElements()
    {
        foreach (XFile f in _files.Where(f => f.Doc != null && (f.Kind == Kind.Defs || f.Kind == Kind.Patches)))
        {
            foreach (XElem e in f.Doc!.AllElements().Where(e => e.Name == "defName" && e.Parent != null))
            {
                var lt = f.Doc.LeafText(e);
                if (lt == null)
                {
                    continue;
                }
                XElem def = e.Parent!;
                string type = def.Attr("Class")?.Value ?? def.Name;
                yield return (f, def, type, lt.Value.Value, lt.Value.Start);
            }
        }
    }

    private void ValidateNames()
    {
        var defs = DefNameElements().ToList();
        var byName = defs.GroupBy(d => d.Name).ToDictionary(g => g.Key, g => g.ToList());
        foreach (DefRename d in _m.Defs)
        {
            var hits = byName.GetValueOrDefault(d.Old) ?? new();
            var mine = hits.Where(h => RootMatches(d, h.Type)).ToList();
            if (mine.Count == 0)
            {
                _report.Error("def-missing", $"{d.DefType} {d.Old} ({d.Entry.Module}) is not defined in 1.7 content");
            }
            var otherRoots = hits.Where(h => !RootMatches(d, h.Type)).Select(h => _types.RootOrName(h.Type)).Distinct().ToList();
            if (otherRoots.Count > 0 && !d.Shared)
            {
                _report.Error("def-shared-unflagged", $"{d.DefType} {d.Old} shares its name with {string.Join(", ", otherRoots)}: set sharedNameWithOtherDefTypes and list explicitRefs");
            }
            if (otherRoots.Count == 0 && d.Shared)
            {
                _report.Warn("def-shared-flag-unneeded", $"{d.DefType} {d.Old} is flagged shared but no other def type uses the name");
            }
            var clash = (byName.GetValueOrDefault(d.New) ?? new()).Where(h => RootMatches(d, h.Type)).ToList();
            if (clash.Count > 0 && !_m.Defs.Any(o => o.Old == d.New && RootMatches(o, clash[0].Type)))
            {
                _report.Error("def-target-exists", $"{d.DefType} {d.New} already exists ({clash[0].File.Rel}:{LineOf(clash[0].File, clash[0].Pos)})");
            }
            foreach (var (o, _) in d.AllNames().Skip(1))
            {
                foreach (var h in byName.GetValueOrDefault(o) ?? new())
                {
                    _report.Error("def-derived-explicit", $"{o} is defined explicitly ({h.File.Rel}:{LineOf(h.File, h.Pos)}); it would be renamed only as a reference — add a def entry for it");
                }
            }
        }
        var names = _files.Where(f => f.Doc != null && (f.Kind == Kind.Defs || f.Kind == Kind.Patches))
            .SelectMany(f => f.Doc!.AllElements().Select(e => e.Attr("Name")?.Value)).Where(v => v != null).ToHashSet(StringComparer.Ordinal);
        foreach (var kv in _m.AbstractNames)
        {
            if (!names.Contains(kv.Key))
            {
                _report.Error("abstract-missing", $"abstract Name {kv.Key} is not declared in 1.7 content");
            }
            if (names.Contains(kv.Value) && !_m.AbstractNames.ContainsKey(kv.Value))
            {
                _report.Error("abstract-target-exists", $"abstract Name {kv.Value} already exists");
            }
        }
        var keysByLang = _files.Where(f => f.Kind == Kind.Keyed && f.Doc?.Root != null)
            .GroupBy(f => f.Lang!)
            .ToDictionary(g => g.Key, g => g.SelectMany(f => f.Doc!.Root!.Children.Select(c => c.Name)).ToHashSet(StringComparer.Ordinal));
        foreach (var kv in _m.Keyed)
        {
            if (!keysByLang.Values.Any(s => s.Contains(kv.Key)))
            {
                _report.Error("keyed-missing", $"Keyed key {kv.Key} is not defined in any language");
            }
            foreach (var (lang, set) in keysByLang)
            {
                if (set.Contains(kv.Value) && !_m.Keyed.ContainsKey(kv.Value))
                {
                    _report.Error("keyed-target-exists", $"Keyed key {kv.Value} already exists in {lang}");
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Moves
    // ------------------------------------------------------------------------------------------------------------------

    private void PlanMoves()
    {
        var byRel = _files.ToDictionary(f => f.Rel, StringComparer.OrdinalIgnoreCase);
        var explicitOld = new HashSet<string>(_m.ContentFileMoves.Select(x => x.Old), StringComparer.OrdinalIgnoreCase);
        foreach (var (o, n, module, _) in _m.ContentFileMoves)
        {
            if (!byRel.TryGetValue(o, out XFile? f))
            {
                _report.Error("content-move-missing", $"contentFileMoves: {o} ({module}) does not exist under 1.7/");
                continue;
            }
            if (!string.Equals(Path.GetExtension(o), Path.GetExtension(n), StringComparison.OrdinalIgnoreCase))
            {
                _report.Error("content-move-invalid", $"{o} -> {n}: extension changes");
            }
            f.OutRel = n;
        }
        // DefInjected files follow a renamed Defs file when the old file name is unambiguous within its content root.
        foreach (var (o, n, _, _) in _m.ContentFileMoves.ToList())
        {
            Match mo = RootRx.Match(o), mn = RootRx.Match(n);
            if (!mo.Success || !mn.Success || !mo.Groups[2].Value.StartsWith("Defs/", StringComparison.Ordinal))
            {
                continue;
            }
            string oldStem = Path.GetFileName(o), newStem = Path.GetFileName(n);
            if (string.Equals(oldStem, newStem, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string root = mo.Groups[1].Value;
            int sameStem = _files.Count(f => f.Kind == Kind.Defs && f.Root == root && string.Equals(Path.GetFileName(f.Rel), oldStem, StringComparison.OrdinalIgnoreCase));
            var followers = _files.Where(f => f.Kind == Kind.DefInjected && f.Root == root && string.Equals(Path.GetFileName(f.Rel), oldStem, StringComparison.OrdinalIgnoreCase) && !explicitOld.Contains(f.Rel)).ToList();
            if (followers.Count == 0)
            {
                continue;
            }
            if (sameStem != 1)
            {
                _report.Warn("definjected-follow-ambiguous", $"{sameStem} Defs files are named {oldStem} under {root}; list the DefInjected moves explicitly: {string.Join(", ", followers.Select(f => f.Rel))}");
                continue;
            }
            foreach (XFile f in followers)
            {
                string target = f.Rel.Substring(0, f.Rel.Length - oldStem.Length) + newStem;
                f.OutRel = target;
                _m.ContentFileMoves.Add((f.Rel, target, "(auto)", true));
            }
        }
        // DefInjected folders named after a renamed Def subclass.
        foreach (XFile f in _files.Where(f => f.Kind == Kind.DefInjected && f.DefInjectedFolder != null))
        {
            string folder = f.DefInjectedFolder!;
            string? newFolder = _t.TypeFull.TryGetValue(folder, out string? nf) ? nf : _t.TypeShort.TryGetValue(folder, out string? ns) ? ns : null;
            if (newFolder == null)
            {
                continue;
            }
            string marker = "/DefInjected/" + folder + "/";
            int i = f.OutRel.IndexOf(marker, StringComparison.Ordinal);
            if (i >= 0)
            {
                f.OutRel = f.OutRel.Substring(0, i) + "/DefInjected/" + newFolder + "/" + f.OutRel.Substring(i + marker.Length);
                _m.ContentFileMoves.Add((f.Rel, f.OutRel, "(auto: Def type folder)", true));
            }
        }
        foreach (XFile f in _files.Where(f => !string.Equals(f.Rel, f.OutRel, StringComparison.OrdinalIgnoreCase)))
        {
            if (!RepoPaths.IsEditable(f.OutRel))
            {
                _report.Error("content-move-invalid", $"{f.Rel} -> {f.OutRel}: target outside 1.7/");
            }
            if (File.Exists(_repo.Full(f.OutRel)) && !_files.Any(o => string.Equals(o.Rel, f.OutRel, StringComparison.OrdinalIgnoreCase) && !string.Equals(o.Rel, o.OutRel, StringComparison.OrdinalIgnoreCase)))
            {
                _report.Error("content-move-target-exists", $"{f.Rel} -> {f.OutRel}: target exists");
            }
            Moves.Add((f.Rel, f.OutRel));
        }
        foreach (var g in _files.GroupBy(f => f.OutRel, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            _report.Error("content-move-collision", $"several files end up at {g.Key}");
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Defs / Patches / other XML
    // ------------------------------------------------------------------------------------------------------------------

    private static bool SkipDefRefContext(XElem e) =>
        e.Name.EndsWith("Path", StringComparison.Ordinal) || e.Name.EndsWith("path", StringComparison.Ordinal) || e.Name.EndsWith("Paths", StringComparison.Ordinal)
        || e.Name is "label" or "labelShort" or "description" or "jobString" or "reportString" or "labelNoun" or "customLabel" or "text";

    private void ElementRules(XFile f)
    {
        XDoc doc = f.Doc!;
        Dictionary<XElem, XmlSchema.Slot> slots = f.Kind == Kind.Defs && _schema != null ? _schema.Analyze(doc) : new();
        foreach (var (owner, oldField, newField) in _fieldRenames)
        {
            foreach (var (e, slot) in slots.Where(kv => kv.Key.Name == oldField && kv.Value.Field == oldField && kv.Value.Owner != null && CSharp.CSharpPass.MetadataKey(kv.Value.Owner) == owner))
            {
                Edit(f, e.NameStart, e.Name.Length, newField, "xml-field");
                if (e.EndNameStart >= 0)
                {
                    Edit(f, e.EndNameStart, e.Name.Length, newField, "xml-field");
                }
            }
        }
        if (f.Kind == Kind.Patches)
        {
            foreach (var (owner, oldField, _) in _fieldRenames)
            {
                foreach (XElem e in doc.AllElements().Where(e => e.Name == oldField || e.Name == "xpath" && doc.LeafText(e)?.Value.Contains("/" + oldField, StringComparison.Ordinal) == true))
                {
                    _report.Warn("xml-field-in-patch", $"patch mentions field {oldField} (renamed on {owner}); check the xpath/value by hand", f.Rel, LineOf(f, e.NameStart));
                }
            }
        }
        foreach (XElem e in doc.AllElements())
        {
            // Def subclass element names (<Mugirl.SlaveApparelDef>), anywhere (Defs root or Patches <value>).
            if (_t.TypeFull.TryGetValue(e.Name, out string? newElem))
            {
                Edit(f, e.NameStart, e.Name.Length, newElem, "xml-element-type");
                if (e.EndNameStart >= 0)
                {
                    Edit(f, e.EndNameStart, e.Name.Length, newElem, "xml-element-type");
                }
            }
            // defNames used as element names: dictionary keys such as PrefabDef <things><WallRopeHitch>, statBases, skills.
            else if (_t.DefByOld.ContainsKey(e.Name) && e.Parent != null)
            {
                string? target = DefTargetAt(f, e.Name, LineOf(f, e.NameStart), out string refKey);
                if (target != null)
                {
                    Edit(f, e.NameStart, e.Name.Length, target, "xml-element-def");
                    if (e.EndNameStart >= 0)
                    {
                        Edit(f, e.EndNameStart, e.Name.Length, target, "xml-element-def");
                    }
                }
                else if (!_t.IsNotRef(e.Name, refKey))
                {
                    _report.Warn("xml-element-def-shared", $"element <{e.Name}> (dictionary key?) names a def shared by several def types; not renamed — add {refKey} to explicitRefs", f.Rel, LineOf(f, e.NameStart));
                }
            }
            foreach (XAttr a in e.Attrs)
            {
                if (a.Name == "Class" && _t.TypeFullRx != null)
                {
                    foreach (Match mt in _t.TypeFullRx.Matches(a.RawValue))
                    {
                        Edit(f, a.ValueStart + mt.Index, mt.Length, _t.TypeFull[mt.Value], "xml-class");
                    }
                }
                else if ((a.Name == "Name" || a.Name == "ParentName") && _t.Abstract.TryGetValue(a.RawValue.Trim(), out string? newAbs))
                {
                    int lead = a.RawValue.Length - a.RawValue.TrimStart().Length;
                    Edit(f, a.ValueStart + lead, a.RawValue.Trim().Length, newAbs, "xml-abstract");
                }
            }
            var lt = doc.LeafText(e);
            if (lt == null)
            {
                continue;
            }
            var (start, length, value) = lt.Value;
            int line = LineOf(f, start);
            if (e.Name == "xpath" && f.Kind == Kind.Patches)
            {
                XPathRules(f, start, f.Data.Text.Substring(start, length), line);
                continue;
            }
            if (_t.TypeFull.TryGetValue(value, out string? newType))
            {
                Edit(f, start, length, newType, "xml-type-text");
                continue;
            }
            if (_t.TypeFullRx != null && Regex.IsMatch(value, @"^[\w.+`\[\], ]+$") && _t.TypeFullRx.IsMatch(value))
            {
                // Type-valued text with generic arguments or a nested type of a renamed outer type.
                string raw = f.Data.Text.Substring(start, length);
                foreach (Match mt in _t.TypeFullRx.Matches(raw))
                {
                    Edit(f, start + mt.Index, mt.Length, _t.TypeFull[mt.Value], "xml-type-text");
                }
                continue;
            }
            if (e.Name == "defName")
            {
                DefNameRule(f, e, start, length, value);
                continue;
            }
            if (_t.Keys.TryGetValue(value, out string? newKey))
            {
                Edit(f, start, length, newKey, "xml-key-text");
                continue;
            }
            if (slots.TryGetValue(e, out XmlSchema.Slot? slot) && (_t.DefByOld.ContainsKey(value) || _t.Abstract.ContainsKey(value)))
            {
                if (slot.Kind == XmlSchema.Kind.DefRef)
                {
                    TypedDefRef(f, e, start, length, value, slot.DefRoot!, line);
                    continue;
                }
                if (slot.Kind == XmlSchema.Kind.String && !slot.OwnerIsMod)
                {
                    _report.Info("xml-string-not-def", $"<{e.Name}>{value}</{e.Name}> loads into string field {slot.Owner?.ToDisplayString()}.{slot.Field} (not a def reference; left as is)", f.Rel, line);
                    continue;
                }
            }
            if (_t.DefByOld.ContainsKey(value))
            {
                DefRefRule(f, e, start, length, value, line);
            }
            else if (_t.Abstract.ContainsKey(value))
            {
                _report.Warn("xml-abstract-as-text", $"text equals the abstract Name {value} (not replaced)", f.Rel, line, f.Lines.LineText(line).Trim());
            }
        }
        foreach (XElem e in doc.AllElements())
        {
            foreach (var (s, l) in e.CData)
            {
                ReportMentions(f, f.Data.Text.Substring(s, l), s, "xml-cdata-mentions");
            }
        }
    }

    private void DefNameRule(XFile f, XElem e, int start, int length, string value)
    {
        if (!_t.DefByOld.TryGetValue(value, out var entries) || e.Parent == null)
        {
            return;
        }
        string type = e.Parent.Attr("Class")?.Value ?? e.Parent.Name;
        var mine = entries.Where(x => x.Def.Old == value && RootMatches(x.Def, type)).ToList();
        if (mine.Count == 1)
        {
            Edit(f, start, length, mine[0].New, "xml-defname");
        }
        else if (mine.Count > 1)
        {
            _report.Error("xml-defname-ambiguous", $"<defName>{value}</defName> of {type} matches several def renames", f.Rel, LineOf(f, start));
        }
    }

    // Target of an old def name at a location: an explicitRef there wins; otherwise only an unshared, single-target name.
    private string? DefTargetAt(XFile f, string value, int line, out string refKey)
    {
        refKey = f.Rel + ":" + line;
        if (_t.IsNotRef(value, refKey))
        {
            return null;
        }
        var entries = _t.DefByOld[value];
        string key = refKey;
        var explicitHits = entries.Where(x => x.Def.ExplicitRefs.Contains(key)).ToList();
        if (explicitHits.Count == 1)
        {
            explicitHits[0].Def.UsedExplicitRefs.Add(refKey);
            return explicitHits[0].New;
        }
        return _t.UniqueDefTarget(value, out _);
    }

    // A reference whose def type is known from the schema: only renames of that Def database type apply.
    private void TypedDefRef(XFile f, XElem e, int start, int length, string value, string root, int line)
    {
        string refKey = f.Rel + ":" + line;
        if (!_t.DefByOld.TryGetValue(value, out var entries))
        {
            return;
        }
        if (_t.IsNotRef(value, refKey))
        {
            return;
        }
        var mine = entries.Where(x => DerivedRoot(x.Def, value) == root).ToList();
        if (mine.Count == 1)
        {
            Edit(f, start, length, mine[0].New, "xml-def-ref-typed");
            mine[0].Def.UsedExplicitRefs.Add(refKey);
            return;
        }
        if (mine.Count == 0)
        {
            _report.Info("xml-def-ref-other-type", $"<{e.Name}>{value}</{e.Name}> is a {root} reference; the renamed def(s) of that name are {string.Join(", ", entries.Select(x => x.Def.Root ?? x.Def.DefType))} (left as is)", f.Rel, line);
            return;
        }
        _report.Error("xml-def-ref-ambiguous", $"<{e.Name}>{value}</{e.Name}> matches several {root} renames", f.Rel, line);
    }

    private static string? DerivedRoot(DefRename d, string name) =>
        name == d.Old ? d.Root : DefRename.DerivedRoot(name);

    private void DefRefRule(XFile f, XElem e, int start, int length, string value, int line)
    {
        if (SkipDefRefContext(e))
        {
            _report.Info("xml-def-like-text-skipped", $"<{e.Name}> text equals old defName {value}; not a def reference (left as is)", f.Rel, line);
            return;
        }
        var entries = _t.DefByOld[value];
        string? target = DefTargetAt(f, value, line, out string refKey);
        if (target == null && _t.IsNotRef(value, refKey))
        {
            _report.Info("xml-not-ref", $"<{e.Name}>{value}</{e.Name}> is listed in notRefs (not a reference to the renamed def)", f.Rel, line);
            return;
        }
        if (target != null)
        {
            Edit(f, start, length, target, "xml-def-ref");
            if (e.Name.EndsWith("Tags", StringComparison.Ordinal) || e.Parent?.Name.EndsWith("Tags", StringComparison.Ordinal) == true || e.Parent?.Name == "tags")
            {
                _report.Warn("xml-def-ref-in-tags", $"renamed {value} inside a tag list ({e.Path}); confirm it is a def reference, not a tag", f.Rel, line);
            }
            return;
        }
        _report.Warn("xml-def-ref-shared", $"<{e.Name}>{value}</{e.Name}> may refer to {string.Join(" or ", entries.Select(x => x.Def.DefType))}; not renamed — add {refKey} to explicitRefs of the intended def", f.Rel, line, f.Lines.LineText(line).Trim());
    }

    private void XPathRules(XFile f, int start, string raw, int line)
    {
        var taken = new List<(int S, int E)>();
        if (_t.TypeFullRx != null)
        {
            foreach (Match mt in _t.TypeFullRx.Matches(raw))
            {
                Edit(f, start + mt.Index, mt.Length, _t.TypeFull[mt.Value], "xpath-type");
                taken.Add((mt.Index, mt.Index + mt.Length));
            }
        }
        if (_t.DefRx != null)
        {
            foreach (Match mt in _t.DefRx.Matches(raw))
            {
                if (taken.Any(t => mt.Index < t.E && mt.Index + mt.Length > t.S))
                {
                    continue;
                }
                var entries = _t.DefByOld[mt.Value];
                string refKey = f.Rel + ":" + line;
                if (_t.IsNotRef(mt.Value, refKey))
                {
                    taken.Add((mt.Index, mt.Index + mt.Length));
                    continue;
                }
                var explicitHits = entries.Where(x => x.Def.ExplicitRefs.Contains(refKey)).ToList();
                string? target = explicitHits.Count == 1 ? explicitHits[0].New : _t.UniqueDefTarget(mt.Value, out _);
                if (explicitHits.Count == 1)
                {
                    explicitHits[0].Def.UsedExplicitRefs.Add(refKey);
                }
                if (target != null)
                {
                    Edit(f, start + mt.Index, mt.Length, target, "xpath-def");
                }
                else
                {
                    _report.Warn("xpath-def-shared", $"xpath mentions {mt.Value}, shared by several def types; not renamed — add {refKey} to explicitRefs", f.Rel, line, raw.Trim());
                }
                taken.Add((mt.Index, mt.Index + mt.Length));
            }
        }
        if (_t.AbstractRx != null)
        {
            foreach (Match mt in _t.AbstractRx.Matches(raw))
            {
                if (!taken.Any(t => mt.Index < t.E && mt.Index + mt.Length > t.S))
                {
                    Edit(f, start + mt.Index, mt.Length, _t.Abstract[mt.Value], "xpath-abstract");
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Languages
    // ------------------------------------------------------------------------------------------------------------------

    private static readonly Regex HandleIndex = new(@"^(?<name>.+?)(?<idx>-\d+)?$", RegexOptions.CultureInvariant);

    private void DefInjectedRules(XFile f)
    {
        XElem? root = f.Doc!.Root;
        if (root == null || f.DefInjectedFolder == null)
        {
            return;
        }
        foreach (XElem c in root.Children)
        {
            string[] segs = c.Name.Split('.');
            bool changed = false;
            int line = LineOf(f, c.NameStart);
            if (_t.DefByOld.TryGetValue(segs[0], out var entries))
            {
                string folderRoot = _types.RootOrName(f.DefInjectedFolder);
                string first = segs[0];
                // Derived names live in other Def databases: Make_X is a RecipeDef, Blueprint_/Frame_/Corpse_/Meat_ are ThingDefs.
                var mine = entries.Where(x => x.Def.Old == first
                    ? RootMatches(x.Def, f.DefInjectedFolder)
                    : folderRoot == DefRename.DerivedRoot(first)).ToList();
                if (mine.Count == 1)
                {
                    segs[0] = mine[0].New;
                    changed = true;
                }
                else if (mine.Count > 1)
                {
                    _report.Error("definj-ambiguous", $"key {c.Name} matches several def renames", f.Rel, line);
                }
            }
            foreach (var (_, oldField, newField) in _fieldRenames)
            {
                for (int i = 1; i < segs.Length; i++)
                {
                    if (segs[i] == oldField)
                    {
                        segs[i] = newField;
                        changed = true;
                        _report.Info("definj-field-segment", $"key segment {oldField} renamed with its field (verify the owner type)", f.Rel, line);
                    }
                }
            }
            for (int i = 1; i < segs.Length; i++)
            {
                Match hm = HandleIndex.Match(segs[i]);
                string name = hm.Groups["name"].Value;
                if (_t.TypeShort.TryGetValue(name, out string? newShort))
                {
                    segs[i] = newShort + hm.Groups["idx"].Value;
                    changed = true;
                }
            }
            if (!changed)
            {
                continue;
            }
            string newKey = string.Join(".", segs);
            Edit(f, c.NameStart, c.Name.Length, newKey, "definj-key");
            if (c.EndNameStart >= 0)
            {
                Edit(f, c.EndNameStart, c.Name.Length, newKey, "definj-key");
            }
        }
    }

    private void KeyedRules(XFile f)
    {
        XElem? root = f.Doc!.Root;
        if (root == null)
        {
            return;
        }
        foreach (XElem c in root.Children)
        {
            if (_t.Keys.TryGetValue(c.Name, out string? newKey))
            {
                Edit(f, c.NameStart, c.Name.Length, newKey, "keyed-key");
                if (c.EndNameStart >= 0)
                {
                    Edit(f, c.EndNameStart, c.Name.Length, newKey, "keyed-key");
                }
            }
            foreach (var (s, l) in c.Texts)
            {
                string text = f.Data.Text.Substring(s, l);
                if (_t.KeyRx?.IsMatch(text) == true)
                {
                    _report.Warn("keyed-value-mentions-key", "translation text mentions an old Keyed key", f.Rel, LineOf(f, s), text.Trim());
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------

    private void CommentRules(XFile f, bool edit)
    {
        foreach (var (s, l) in f.Doc!.Comments)
        {
            string text = f.Data.Text.Substring(s, l);
            var taken = new List<(int S, int E)>();
            if (edit && _t.TypeFullRx != null)
            {
                foreach (Match mt in _t.TypeFullRx.Matches(text))
                {
                    Edit(f, s + mt.Index, mt.Length, _t.TypeFull[mt.Value], "xml-comment-type");
                    taken.Add((mt.Index, mt.Index + mt.Length));
                }
            }
            if (edit && _t.DefRx != null)
            {
                foreach (Match mt in _t.DefRx.Matches(text))
                {
                    if (taken.Any(t => mt.Index < t.E && mt.Index + mt.Length > t.S))
                    {
                        continue;
                    }
                    int line = LineOf(f, s + mt.Index);
                    string refKey = f.Rel + ":" + line;
                    if (_t.IsNotRef(mt.Value, refKey))
                    {
                        taken.Add((mt.Index, mt.Index + mt.Length));
                        continue;
                    }
                    var explicitHits = _t.DefByOld[mt.Value].Where(x => x.Def.ExplicitRefs.Contains(refKey)).ToList();
                    string? target = explicitHits.Count == 1 ? explicitHits[0].New : _t.UniqueDefTarget(mt.Value, out _);
                    if (target != null)
                    {
                        Edit(f, s + mt.Index, mt.Length, target, "xml-comment-def");
                        if (explicitHits.Count == 1)
                        {
                            explicitHits[0].Def.UsedExplicitRefs.Add(refKey);
                        }
                        taken.Add((mt.Index, mt.Index + mt.Length));
                    }
                }
            }
            if (edit && _t.AbstractRx != null)
            {
                foreach (Match mt in _t.AbstractRx.Matches(text))
                {
                    if (!taken.Any(t => mt.Index < t.E && mt.Index + mt.Length > t.S))
                    {
                        Edit(f, s + mt.Index, mt.Length, _t.Abstract[mt.Value], "xml-comment-abstract");
                        taken.Add((mt.Index, mt.Index + mt.Length));
                    }
                }
            }
            ReportMentions(f, text, s, "xml-comment-mentions", taken, includeTypes: !edit);
        }
    }

    private void ReportMentions(XFile f, string text, int offset, string kind, List<(int S, int E)>? taken = null, bool includeTypes = true)
    {
        var rxs = new List<Regex?> { _t.DefRx, _t.KeyRx, _t.AbstractRx, _t.DistinctiveShortRx };
        if (includeTypes)
        {
            rxs.Add(_t.TypeFullRx);
        }
        foreach (Regex? rx in rxs)
        {
            if (rx == null)
            {
                continue;
            }
            foreach (Match mt in rx.Matches(text))
            {
                if (taken != null && taken.Any(t => mt.Index < t.E && mt.Index + mt.Length > t.S))
                {
                    continue;
                }
                _report.Info(kind, $"mentions {mt.Value} (not rewritten)", f.Rel, LineOf(f, offset + mt.Index));
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------

    private void Finish()
    {
        foreach (XFile f in _files)
        {
            bool moved = !string.Equals(f.Rel, f.OutRel, StringComparison.OrdinalIgnoreCase);
            if (f.Edits.Count == 0 && !moved)
            {
                continue;
            }
            List<TextEdit> edits;
            try
            {
                edits = TextEdits.Normalize(f.Edits, f.Rel);
            }
            catch (ToolException ex)
            {
                _report.Error("xml-edit-conflict", ex.Message, f.Rel);
                continue;
            }
            string text = TextEdits.Apply(f.Data.Text, edits);
            if (f.Doc != null && edits.Count > 0)
            {
                try
                {
                    using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
                    while (reader.Read())
                    {
                    }
                }
                catch (XmlException ex)
                {
                    _report.Error("xml-result-invalid", $"edited XML is not well-formed: {ex.Message}", f.Rel);
                    continue;
                }
            }
            foreach (TextEdit e in edits)
            {
                var (line, col) = f.Lines.Get(e.Start);
                _report.Edits.Add(new EditRecord
                {
                    File = f.Rel,
                    NewFile = moved ? f.OutRel : null,
                    Line = line,
                    Column = col,
                    Kind = e.Kind,
                    Before = f.Data.Text.Substring(e.Start, e.Length),
                    After = e.NewText,
                    BeforeLine = f.Lines.LineText(line).Trim(),
                });
            }
            Writes.Add(new PendingWrite(_repo.Full(f.OutRel), text, f.Data.Bom, moved ? f.Full : null));
            if (moved)
            {
                _report.FileOps.Add(new FileOp { Op = "move", From = f.Rel, To = f.OutRel, Note = _m.ContentFileMoves.FirstOrDefault(x => string.Equals(x.Old, f.Rel, StringComparison.OrdinalIgnoreCase)).Module });
            }
        }
    }
}
