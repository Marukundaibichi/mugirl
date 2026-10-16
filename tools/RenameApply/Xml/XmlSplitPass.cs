using System.Text;
using System.Text.RegularExpressions;

namespace RenameApply.Xml;

// R2 file splits: top-level Def elements / Patch operations / DefInjected entries / Keyed keys move between files with
// their leading comments and blank lines (a "block" = everything from the end of the previous sibling's line to the end
// of the element's line). Targets are appended before the closing root tag (existing files) or created with the source's
// prolog and root tag. DefInjected entries of moved defs follow into the DefInjected file named after the target, in
// every language of the same content root. Emptied sources are deleted.
public sealed class XmlSplitPass
{
    private readonly RepoPaths _repo;
    private readonly RenameModel _m;
    private readonly RunReport _report;
    private readonly Dictionary<string, FileState> _files = new(StringComparer.OrdinalIgnoreCase);   // rel -> state

    public List<PendingWrite> Writes { get; } = new();
    public HashSet<string> Touched { get; } = new(StringComparer.OrdinalIgnoreCase);

    private sealed class FileState
    {
        public required string Rel { get; init; }
        public TextFileData? Data { get; init; }          // null: file is created
        public XDoc? Doc { get; init; }
        public List<TextEdit> Removals { get; } = new();
        public List<string> Appended { get; } = new();    // blocks (already ending with a newline)
        public string? CreatePrologFrom { get; set; }     // rel of the file whose prolog/root tag a created file copies
        public string? Header { get; set; }               // created file: text after the root start tag
        public HashSet<XElem> Taken { get; } = new();
    }

    public XmlSplitPass(RepoPaths repo, RenameModel m, RunReport report)
    {
        _repo = repo;
        _m = m;
        _report = report;
    }

    public bool HasWork => _m.Spec.XmlSplits.Count > 0 || _m.Spec.KeyedSplits.Count > 0;

    private FileState? Load(string rel, bool mayCreate, string? prologFrom)
    {
        rel = RepoPaths.Norm(rel);
        if (_files.TryGetValue(rel, out FileState? st))
        {
            return st;
        }
        string full = _repo.Full(rel);
        if (!RepoPaths.IsEditable(rel))
        {
            _report.Error("split-path", $"{rel} is outside the editable zones");
            return null;
        }
        if (File.Exists(full))
        {
            TextFileData data = TextFileData.Read(full);
            XDoc doc;
            try
            {
                doc = XDoc.Parse(data.Text);
            }
            catch (FormatException ex)
            {
                _report.Error("split-parse", ex.Message, rel);
                return null;
            }
            st = new FileState { Rel = rel, Data = data, Doc = doc };
        }
        else if (mayCreate)
        {
            st = new FileState { Rel = rel, CreatePrologFrom = prologFrom };
        }
        else
        {
            _report.Error("split-source-missing", $"{rel} does not exist");
            return null;
        }
        _files[rel] = st;
        return st;
    }

    // Block of a top-level element: from the start of the line after the previous sibling (or the root start tag) to the end
    // of the element's own line.
    private static (int Start, int End) BlockOf(XDoc doc, XElem e)
    {
        string t = doc.Text;
        XElem root = doc.Root!;
        int idx = root.Children.IndexOf(e);
        int prevEnd = idx == 0 ? root.ContentStart : root.Children[idx - 1].End;
        int start = t.IndexOf('\n', prevEnd);
        start = start < 0 || start > e.Start ? prevEnd : start + 1;
        int end = e.End;
        while (end < t.Length && (t[end] == ' ' || t[end] == '\t' || t[end] == '\r'))
        {
            end++;
        }
        if (end < t.Length && t[end] == '\n')
        {
            end++;
        }
        return (start, end);
    }

    private static string? DefNameOf(XElem e, XDoc doc) =>
        e.Children.FirstOrDefault(c => c.Name == "defName") is { } dn ? doc.LeafText(dn)?.Value : null;

    public void Run()
    {
        foreach (XmlSplitEntry s in _m.Spec.XmlSplits)
        {
            Split(s);
        }
        foreach (KeyedSplitEntry k in _m.Spec.KeyedSplits)
        {
            KeyedSplit(k);
        }
        Finish();
    }

    private static readonly Regex ContentRootRx = new(@"^(1\.7(?:/Mods/[^/]+)?)/(.*)$", RegexOptions.CultureInvariant);

    private void Split(XmlSplitEntry s)
    {
        FileState? src = Load(s.Source, false, null);
        if (src?.Doc?.Root == null)
        {
            return;
        }
        XDoc doc = src.Doc;
        string rootName = doc.Root.Name;
        Match cm = ContentRootRx.Match(src.Rel);
        string contentRoot = cm.Success ? cm.Groups[1].Value : "1.7";
        bool defInjectedSource = src.Rel.Contains("/DefInjected/", StringComparison.Ordinal);
        foreach (XmlSplitTarget target in s.Targets)
        {
            string targetRel = RepoPaths.Norm(target.File);
            if (string.Equals(targetRel, src.Rel, StringComparison.OrdinalIgnoreCase))
            {
                continue;   // stays
            }
            FileState? dst = Load(targetRel, true, src.Rel);
            if (dst == null)
            {
                continue;
            }
            if (target.Header != null && dst.Data == null)
            {
                dst.Header = target.Header;
            }
            var chosen = new List<XElem>();
            if (target.Operations != null)
            {
                for (int i = 0; i < target.Operations.Count; i++)
                {
                    int index = target.Operations[i];
                    if (index < 1 || index > doc.Root.Children.Count)
                    {
                        _report.Error("split-operation-missing", $"{src.Rel}: operation #{index} does not exist");
                        continue;
                    }
                    XElem op = doc.Root.Children[index - 1];
                    string fp = target.OperationFingerprints != null && i < target.OperationFingerprints.Count ? target.OperationFingerprints[i] : "";
                    if (fp.Length > 0 && !doc.Text.AsSpan(op.Start, op.End - op.Start).Contains(fp, StringComparison.Ordinal))
                    {
                        _report.Error("split-operation-fingerprint", $"{src.Rel}: operation #{index} does not contain '{fp}' (stale spec)");
                        continue;
                    }
                    chosen.Add(op);
                }
            }
            foreach (string sel in target.Defs ?? new())
            {
                var hits = doc.Root.Children.Where(c => Matches(c, doc, sel, defInjectedSource)).ToList();
                if (hits.Count == 0)
                {
                    _report.Error("split-def-missing", $"{src.Rel}: no element for '{sel}'");
                    continue;
                }
                chosen.AddRange(hits);
            }
            foreach (XElem e in doc.Root.Children.Where(chosen.Contains))   // source order
            {
                if (!src.Taken.Add(e))
                {
                    _report.Info("split-element-already-moved", $"{src.Rel}: element {e.Name} already moved (DefInjected follow or another target)");
                    continue;
                }
                var (bs, be) = BlockOf(doc, e);
                src.Removals.Add(new TextEdit(bs, be - bs, "", "split-out"));
                dst.Appended.Add(doc.Text.Substring(bs, be - bs));
                string name = defInjectedSource ? e.Name : (DefNameOf(e, doc) ?? e.Attr("Name")?.Value ?? $"#{doc.Root.Children.IndexOf(e) + 1}");
                _report.Maps.Splits.Add(new SplitMapEntry { Kind = rootName == "Patch" ? "operation" : defInjectedSource ? "definjected" : "def", Name = name, From = src.Rel, To = targetRel });
                if (rootName == "Defs" && !defInjectedSource && DefNameOf(e, doc) is { } dn)
                {
                    FollowDefInjected(contentRoot, RootFolderOf(e), dn, src.Rel, targetRel);
                }
            }
            Touched.Add(src.Rel);
            Touched.Add(targetRel);
        }
    }

    private static bool Matches(XElem e, XDoc doc, string sel, bool defInjected)
    {
        if (defInjected)
        {
            return e.Name.Split('.')[0] == sel;
        }
        if (sel.StartsWith("Name=", StringComparison.Ordinal))
        {
            return e.Attr("Name")?.Value == sel.Substring(5);
        }
        int slash = sel.IndexOf('/');
        string name = slash >= 0 ? sel.Substring(slash + 1) : sel;
        string? type = slash >= 0 ? sel.Substring(0, slash) : null;
        return DefNameOf(e, doc) == name && (type == null || e.Name == type || e.Name.EndsWith("." + type, StringComparison.Ordinal));
    }

    private static string RootFolderOf(XElem def)
    {
        string t = (def.Attr("Class")?.Value ?? def.Name).Split('.').Last();
        return t switch
        {
            "ThingDef_AlienRace" or "SlaveApparelDef" => "ThingDef",
            "AlienBackstoryDef" => "BackstoryDef",
            _ => t,
        };
    }

    // Moves the DefInjected entries of a def (any file of its folder, every language) into the file named after the target.
    private void FollowDefInjected(string contentRoot, string folder, string defName, string sourceRel, string targetRel)
    {
        string langRoot = _repo.Full(contentRoot + "/Languages");
        if (!Directory.Exists(langRoot))
        {
            return;
        }
        string targetName = Path.GetFileName(targetRel);
        foreach (string langDir in Directory.EnumerateDirectories(langRoot).OrderBy(x => x, StringComparer.Ordinal))
        {
            string dir = Path.Combine(langDir, "DefInjected", folder);
            if (!Directory.Exists(dir))
            {
                continue;
            }
            string targetFile = _repo.Rel(Path.Combine(dir, targetName));
            foreach (string f in Directory.EnumerateFiles(dir, "*.xml").OrderBy(x => x, StringComparer.Ordinal))
            {
                string rel = _repo.Rel(f);
                if (string.Equals(rel, targetFile, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                FileState? st = Load(rel, false, null);
                if (st?.Doc?.Root == null)
                {
                    continue;
                }
                var entries = st.Doc.Root.Children.Where(c => c.Name.Split('.')[0] == defName && !st.Taken.Contains(c)).ToList();
                if (entries.Count == 0)
                {
                    continue;
                }
                FileState? dst = Load(targetFile, true, rel);
                if (dst == null)
                {
                    continue;
                }
                foreach (XElem e in entries)
                {
                    st.Taken.Add(e);
                    var (bs, be) = BlockOf(st.Doc, e);
                    st.Removals.Add(new TextEdit(bs, be - bs, "", "split-out"));
                    dst.Appended.Add(st.Doc.Text.Substring(bs, be - bs));
                    _report.Maps.Splits.Add(new SplitMapEntry { Kind = "definjected", Name = e.Name, From = rel, To = targetFile, Language = Path.GetFileName(langDir) });
                }
                Touched.Add(rel);
                Touched.Add(targetFile);
            }
        }
    }

    private void KeyedSplit(KeyedSplitEntry k)
    {
        string root = k.ContentRoot ?? "1.7";
        string langRoot = _repo.Full(root + "/Languages");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string langDir in Directory.EnumerateDirectories(langRoot).OrderBy(x => x, StringComparer.Ordinal))
        {
            string srcRel = _repo.Rel(Path.Combine(langDir, "Keyed", k.Source));
            if (!File.Exists(_repo.Full(srcRel)))
            {
                continue;
            }
            FileState? src = Load(srcRel, false, null);
            if (src?.Doc?.Root == null)
            {
                continue;
            }
            foreach (XElem e in src.Doc.Root.Children)
            {
                seen.Add(e.Name);
                bool delete = k.DeleteKeys?.Contains(e.Name) == true;
                if (!delete && !k.Keys.TryGetValue(e.Name, out _))
                {
                    _report.Warn("keyed-split-unmapped", $"key {e.Name} has no target; it stays in {k.Source}", srcRel);
                    continue;
                }
                var (bs, be) = BlockOf(src.Doc, e);
                src.Removals.Add(new TextEdit(bs, be - bs, "", delete ? "keyed-delete" : "split-out"));
                src.Taken.Add(e);
                if (delete)
                {
                    _report.Maps.Splits.Add(new SplitMapEntry { Kind = "keyed-delete", Name = e.Name, From = srcRel, To = "", Language = Path.GetFileName(langDir) });
                    continue;
                }
                string targetRel = _repo.Rel(Path.Combine(langDir, "Keyed", k.Keys[e.Name]));
                FileState? dst = Load(targetRel, true, srcRel);
                dst?.Appended.Add(src.Doc.Text.Substring(bs, be - bs));
                _report.Maps.Splits.Add(new SplitMapEntry { Kind = "keyed", Name = e.Name, From = srcRel, To = targetRel, Language = Path.GetFileName(langDir) });
                Touched.Add(targetRel);
            }
            Touched.Add(srcRel);
        }
        foreach (string key in k.Keys.Keys.Concat(k.DeleteKeys ?? new()).Where(x => !seen.Contains(x)))
        {
            _report.Error("keyed-split-missing", $"keyedSplits: key {key} is not in any {k.Source}");
        }
    }

    private void Finish()
    {
        foreach (FileState st in _files.Values.OrderBy(f => f.Rel, StringComparer.Ordinal))
        {
            if (st.Removals.Count == 0 && st.Appended.Count == 0)
            {
                continue;
            }
            string full = _repo.Full(st.Rel);
            if (st.Data != null)
            {
                string text = st.Data.Text;
                var edits = new List<TextEdit>(st.Removals);
                if (st.Appended.Count > 0)
                {
                    int close = st.Doc!.Root!.ContentEnd;
                    int lineStart = text.LastIndexOf('\n', Math.Max(0, close - 1)) + 1;
                    bool closeAlone = text.Substring(lineStart, close - lineStart).Trim().Length == 0;
                    string add = string.Concat(st.Appended.Select(b => Normalize(b, st.Data.NewLine)));
                    edits.Add(new TextEdit(closeAlone ? lineStart : close, 0, closeAlone ? add : st.Data.NewLine + add, "split-in"));
                }
                string result = TextEdits.Apply(text, TextEdits.Normalize(edits, st.Rel));
                bool emptied = st.Doc!.Root!.Children.All(st.Taken.Contains) && st.Appended.Count == 0;
                if (emptied)
                {
                    Writes.Add(new PendingWrite(full, null, false, full));
                    _report.FileOps.Add(new FileOp { Op = "delete", From = st.Rel, Note = "every element moved out" });
                    string rest = TextEdits.Apply(text, TextEdits.Normalize(st.Removals, st.Rel));
                    if (Regex.IsMatch(rest, "<!--"))
                    {
                        _report.Info("split-source-comments-dropped", "the emptied source still had comments outside any element; they are dropped with the file", st.Rel);
                    }
                    continue;
                }
                RecordEdits(st, edits);
                Writes.Add(new PendingWrite(full, result, st.Data.Bom, null));
            }
            else
            {
                FileState? proto = st.CreatePrologFrom != null ? Load(st.CreatePrologFrom, false, null) : null;
                if (proto?.Doc?.Root == null)
                {
                    _report.Error("split-create", $"{st.Rel}: no prototype for the new file");
                    continue;
                }
                string t = proto.Data!.Text;
                string nl = proto.Data.NewLine;
                XElem root = proto.Doc.Root;
                string prolog = t.Substring(0, root.Start);
                // keep only the XML declaration from the prolog (file-specific header comments do not apply to the new file)
                Match decl = Regex.Match(prolog, @"^﻿?\s*<\?xml[^>]*\?>");
                string head = (decl.Success ? decl.Value.TrimStart() + nl : "") + t.Substring(root.Start, root.ContentStart - root.Start) + nl;
                string body = (st.Header != null ? Normalize(st.Header, nl) : "") + string.Concat(st.Appended.Select(b => Normalize(b, nl)));
                string text = head + body + "</" + root.Name + ">" + nl;
                Writes.Add(new PendingWrite(full, text, proto.Data.Bom, null));
                _report.FileOps.Add(new FileOp { Op = "create", From = st.CreatePrologFrom!, To = st.Rel, Note = $"split target ({st.Appended.Count} element(s))" });
            }
        }
        // Result must parse.
        foreach (PendingWrite w in Writes.Where(w => w.Text != null))
        {
            try
            {
                XDoc.Parse(w.Text!);
            }
            catch (FormatException ex)
            {
                _report.Error("split-result-invalid", ex.Message, _repo.Rel(w.OutFull));
            }
        }
    }

    // A moved block keeps its lines; a leading blank line separates it from what precedes it in the target.
    private static string Normalize(string block, string nl)
    {
        string b = block.Replace("\r\n", "\n").Replace("\n", nl);
        if (!b.EndsWith(nl, StringComparison.Ordinal))
        {
            b += nl;
        }
        return b;
    }

    private void RecordEdits(FileState st, List<TextEdit> edits)
    {
        var lines = new LineIndex(st.Data!.Text);
        foreach (TextEdit e in edits)
        {
            var (line, col) = lines.Get(e.Start);
            _report.Edits.Add(new EditRecord
            {
                File = st.Rel, Line = line, Column = col, Kind = e.Kind,
                Before = e.Length > 200 ? st.Data.Text.Substring(e.Start, 200) + "..." : st.Data.Text.Substring(e.Start, e.Length),
                After = e.NewText.Length > 200 ? e.NewText.Substring(0, 200) + "..." : e.NewText,
            });
        }
    }
}
