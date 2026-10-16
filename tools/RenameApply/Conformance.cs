using System.Text.RegularExpressions;
using RenameApply.Xml;

namespace RenameApply;

// "Applied log == spec" (critique-validation 2.5 item 5): after every pass and before anything is written, each spec row is
// checked against the FINAL tree (pending writes over the untouched files) with exact path spelling. A row that did not land
// - a case-only file rename dropped by a case-insensitive comparison, a def that kept its old name, a split def that stayed
// behind - is an error ("spec-not-applied"), so nothing is written.
public sealed class ConformancePass
{
    private readonly RepoPaths _repo;
    private readonly RenameModel _m;
    private readonly RunReport _report;
    private readonly Dictionary<string, PendingWrite> _writeByOut = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deleted = new(StringComparer.OrdinalIgnoreCase);
    private int _checked;

    private readonly IReadOnlySet<string> _removedKeys;

    public ConformancePass(RepoPaths repo, RenameModel model, RunReport report, IEnumerable<PendingWrite> writes, IReadOnlySet<string> removedKeys)
    {
        _removedKeys = removedKeys;
        _repo = repo;
        _m = model;
        _report = report;
        foreach (PendingWrite w in writes)
        {
            if (w.Text != null)
            {
                _writeByOut[w.OutFull] = w;
            }
            if (w.DeleteFull != null && !(w.Text != null && string.Equals(w.DeleteFull, w.OutFull, StringComparison.OrdinalIgnoreCase)))
            {
                _deleted.Add(w.DeleteFull);
            }
        }
    }

    // Final spelling of the file at rel (null when it does not exist after the run).
    private string? FinalSpelling(string rel)
    {
        string full = _repo.Full(rel);
        if (_writeByOut.TryGetValue(full, out PendingWrite? w))
        {
            return _repo.Rel(w.OutFull);
        }
        if (_deleted.Contains(full) || !File.Exists(full))
        {
            return null;
        }
        string? dir = Path.GetDirectoryName(full);
        string? onDisk = dir == null ? null : Directory.EnumerateFiles(dir, Path.GetFileName(full)).FirstOrDefault();
        return onDisk == null ? null : _repo.Rel(Path.Combine(dir!, Path.GetFileName(onDisk)));
    }

    private string? FinalText(string rel)
    {
        string full = _repo.Full(rel);
        if (_writeByOut.TryGetValue(full, out PendingWrite? w))
        {
            return w.Text;
        }
        return _deleted.Contains(full) || !File.Exists(full) ? null : TextFileData.Read(full).Text;
    }

    private static readonly Dictionary<string, string> RootOverrides = new(StringComparer.Ordinal)
    {
        ["ThingDef_AlienRace"] = "ThingDef", ["SlaveApparelDef"] = "ThingDef", ["AlienBackstoryDef"] = "BackstoryDef",
    };

    // Database root short name of a Def type or element name (C14).
    private static string RootShort(string type)
    {
        string s = type.Contains('.') ? type[(type.LastIndexOf('.') + 1)..] : type;
        return RootOverrides.TryGetValue(s, out string? r) ? r : s;
    }

    // (root, defName) of the top-level defs of a Defs document (Class= wins over the element name).
    private static IEnumerable<(string Root, string Name)> DefsOf(string text)
    {
        var doc = new System.Xml.XmlDocument();
        try
        {
            doc.LoadXml(text);
        }
        catch (System.Xml.XmlException)
        {
            yield break;
        }
        if (doc.DocumentElement?.Name != "Defs")
        {
            yield break;
        }
        foreach (System.Xml.XmlElement e in doc.DocumentElement.ChildNodes.OfType<System.Xml.XmlElement>())
        {
            string? name = e["defName"]?.InnerText.Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }
            string cls = e.GetAttribute("Class");
            yield return (RootShort(cls.Length > 0 && RootOverrides.ContainsKey(RootShort(cls)) ? cls : e.Name), name);
        }
    }

    private void Fail(string what, string message)
    {
        _report.Error("spec-not-applied", $"{what}: {message}");
    }

    private void ExpectFile(string what, string rel)
    {
        _checked++;
        rel = RepoPaths.Norm(rel);
        string? spelled = FinalSpelling(rel);
        if (spelled == null)
        {
            Fail(what, $"{rel} does not exist after the run");
        }
        else if (!string.Equals(spelled, rel, StringComparison.Ordinal))
        {
            Fail(what, $"{rel} ends up spelled {spelled}");
        }
    }

    private List<string> FinalFiles(Func<string, bool> relFilter)
    {
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in new[] { "1.7", "Source" })
        {
            string dir = _repo.Full(root);
            if (!Directory.Exists(dir))
            {
                continue;
            }
            foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                string rel = _repo.Rel(f);
                if (!RepoPaths.HasSegment(rel, "bin") && !RepoPaths.HasSegment(rel, "obj") && !_deleted.Contains(f))
                {
                    all.Add(rel);
                }
            }
        }
        foreach (PendingWrite w in _writeByOut.Values)
        {
            all.Add(_repo.Rel(w.OutFull));
        }
        return all.Where(relFilter).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    public void Run()
    {
        // C# files and types
        foreach (var (o, n, _) in _m.SourceFileMoves)
        {
            ExpectFile($"fileMoves {o}", n);
        }
        foreach (TypeRename t in _m.Types.Where(t => t.Entry.NewFile != null && !t.Old.IsNested))
        {
            string rel = RepoPaths.Norm(t.Entry.NewFile!);
            ExpectFile($"types[{t.Old.Metadata}].newFile", rel);
            string? text = FinalText(rel);
            if (text != null && !Regex.IsMatch(text, @"\b(?:class|struct|interface|enum|record|delegate)\s+(?:[\w<>\[\],.? ]+\s+)?" + Regex.Escape(t.New.Short) + @"\b"))
            {
                Fail($"types[{t.Old.Metadata}]", $"{rel} does not declare {t.New.Short}");
            }
        }
        foreach (CreateFileEntry c in _m.Spec.CreateFiles)
        {
            ExpectFile($"createFiles {c.Path}", c.Path);
        }
        foreach (CreateTypeEntry c in _m.Spec.CreateTypes)
        {
            ExpectFile($"createTypes {c.FullName}", c.File);
        }
        var csFiles = FinalFiles(r => r.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).Select(r => (Rel: r, Text: FinalText(r) ?? "")).ToList();
        foreach (DissolveTypeEntry d in _m.Spec.DissolveTypes)
        {
            _checked++;
            string ns = d.Type.Contains('.') ? d.Type[..d.Type.LastIndexOf('.')] : "";
            string shortName = d.Type[(d.Type.LastIndexOf('.') + 1)..];
            var still = csFiles.Where(f => Regex.IsMatch(f.Text, @"^\s*namespace\s+" + Regex.Escape(ns) + @"\s*[;{]", RegexOptions.Multiline)
                                           && Regex.IsMatch(f.Text, @"\b(?:class|struct)\s+" + Regex.Escape(shortName) + @"\b")).Select(f => f.Rel).ToList();
            if (still.Count > 0)
            {
                Fail($"dissolveTypes {d.Type}", $"still declared in {string.Join(", ", still)}");
            }
        }

        // Members (the applied log must carry every spec row)
        static string Body(string member) => member.Split('@')[0].Split('(')[0].Trim();
        bool Logged(string member, params string[] kinds) =>
            _report.Maps.Members.Any(x => kinds.Contains(x.Kind) && Body(x.Old) == Body(member));
        foreach (MemberMoveEntry mv in _m.Spec.MemberMoves)
        {
            foreach (string mem in mv.Members)
            {
                _checked++;
                if (!Logged(mem, "move", "copy"))
                {
                    Fail($"memberMoves {mem}", "no move in the applied log");
                }
            }
        }
        foreach (MemberDeleteEntry del in _m.Spec.MemberDeletes)
        {
            foreach (string mem in del.Members)
            {
                _checked++;
                if (!Logged(mem, "delete"))
                {
                    Fail($"memberDeletes {mem}", "no delete in the applied log");
                }
            }
        }
        foreach (MemberRenameEntry r in _m.Spec.MemberRenames)
        {
            _checked++;
            if (!Logged(r.Member, "rename"))
            {
                Fail($"memberRenames {r.Member}", "no rename in the applied log");
            }
        }
        foreach (TextEditEntry te in _m.Spec.TextEdits)
        {
            _checked++;
            string nlOld = te.Old.Replace("\r\n", "\n");
            if (!_report.Edits.Any(e => e.Kind == "text-edit" && e.Before.Replace("\r\n", "\n") == nlOld))
            {
                Fail($"textEdits {te.File}", "not applied");
            }
        }

        // Content: defs, abstract Names, file moves, splits
        var xmlFiles = FinalFiles(r => r.StartsWith("1.7/", StringComparison.OrdinalIgnoreCase) && r.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                                       && !r.Contains("/Languages/", StringComparison.OrdinalIgnoreCase));
        // Defs are compared per Def root (a name may be shared by defs of different types, C13).
        var finalDefs = new HashSet<(string Root, string Name)>();
        var finalNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (string rel in xmlFiles)
        {
            string text = FinalText(rel) ?? "";
            foreach (var def in DefsOf(text))
            {
                finalDefs.Add(def);
            }
            foreach (Match mt in Regex.Matches(text, @"\sName=""([^""]+)"""))
            {
                finalNames.Add(mt.Groups[1].Value);
            }
        }
        var origDefs = new HashSet<(string Root, string Name)>();
        foreach (string f in Directory.EnumerateFiles(_repo.Full("1.7"), "*.xml", SearchOption.AllDirectories))
        {
            if (_repo.Rel(f).Contains("/Languages/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            foreach (var def in DefsOf(File.ReadAllText(f)))
            {
                origDefs.Add(def);
            }
        }
        foreach (DefRename d in _m.Defs)
        {
            _checked++;
            string root = RootShort(d.Root ?? d.DefType);
            if (!origDefs.Contains((root, d.Old)))
            {
                continue;   // not defined by this tree under that root (generated, other mod): references only
            }
            // A Def subclass that is its own root may be renamed in the same run (CorporateIntroductionDef -> ...ConfigDef).
            string finalRoot = _m.Types.FirstOrDefault(t => t.Old.Short == root && t.ShortChanges)?.New.Short ?? root;
            if (!finalDefs.Contains((finalRoot, d.New)))
            {
                Fail($"defs {d.DefType} {d.Old}", $"no {finalRoot} <defName>{d.New}</defName> after the run");
            }
            if (finalDefs.Contains((finalRoot, d.Old)) && !_m.Defs.Any(o => o != d && o.New == d.Old && RootShort(o.Root ?? o.DefType) == root))
            {
                Fail($"defs {d.DefType} {d.Old}", $"{finalRoot} <defName>{d.Old}</defName> is still defined");
            }
        }
        foreach (var (o, n) in _m.AbstractNames)
        {
            _checked++;
            if (!finalNames.Contains(n) || finalNames.Contains(o))
            {
                Fail($"abstractNames {o}", $"Name=\"{n}\" {(finalNames.Contains(n) ? "" : "missing")}{(finalNames.Contains(o) ? $" and Name=\"{o}\" remains" : "")}");
            }
        }
        foreach (var (o, n, _, _) in _m.ContentFileMoves)
        {
            ExpectFile($"contentFileMoves {o}", n);
        }
        foreach (XmlSplitEntry s in _m.Spec.XmlSplits)
        {
            string? srcText = FinalText(s.Source);
            foreach (XmlSplitTarget t in s.Targets)
            {
                ExpectFile($"xmlSplits {s.Source}", t.File);
                string text = FinalText(t.File) ?? "";
                foreach (string def in t.Defs ?? new())
                {
                    _checked++;
                    string name = def.Contains('/') ? def[(def.IndexOf('/') + 1)..] : def;
                    string pattern = name.StartsWith("Name=", StringComparison.Ordinal)
                        ? @"\sName=""" + Regex.Escape(name[5..]) + @""""
                        : s.Source.Contains("/DefInjected/", StringComparison.Ordinal)
                            ? "<" + Regex.Escape(name) + @"\."          // DefInjected keys <defName.field...>
                            : @"<defName>\s*" + Regex.Escape(name) + @"\s*</defName>";
                    if (!Regex.IsMatch(text, pattern))
                    {
                        Fail($"xmlSplits {s.Source}", $"{name} is not in {t.File}");
                    }
                    if (srcText != null && Regex.IsMatch(srcText, pattern))
                    {
                        Fail($"xmlSplits {s.Source}", $"{name} is still in {s.Source}");
                    }
                }
                foreach (string fp in t.OperationFingerprints ?? new())
                {
                    _checked++;
                    if (!text.Contains(fp, StringComparison.Ordinal))
                    {
                        Fail($"xmlSplits {s.Source}", $"operation '{fp}' is not in {t.File}");
                    }
                }
            }
        }
        foreach (KeyedSplitEntry k in _m.Spec.KeyedSplits)
        {
            string root = k.ContentRoot ?? "1.7";
            string langDir = _repo.Full(root + "/Languages");
            if (!Directory.Exists(langDir))
            {
                continue;
            }
            foreach (string lang in Directory.EnumerateDirectories(langDir))
            {
                string srcRel = _repo.Rel(Path.Combine(lang, "Keyed", k.Source));
                if (!File.Exists(_repo.Full(srcRel)))
                {
                    continue;
                }
                string orig = File.ReadAllText(_repo.Full(srcRel));
                foreach (var (key, target) in k.Keys)
                {
                    if (!Regex.IsMatch(orig, "<" + Regex.Escape(key) + @"[\s>/]"))
                    {
                        continue;
                    }
                    _checked++;
                    string tRel = _repo.Rel(Path.Combine(lang, "Keyed", target));
                    if (!Regex.IsMatch(FinalText(tRel) ?? "", "<" + Regex.Escape(key) + @"[\s>/]"))
                    {
                        Fail($"keyedSplits {k.Source}", $"{key} is not in {tRel}");
                    }
                }
            }
        }
        foreach (BaselineAppendEntry b in _m.Spec.BaselineAppends)
        {
            string text = FinalText(b.File) ?? "";
            foreach (string line in b.Lines)
            {
                _checked++;
                if (!text.Replace("\r\n", "\n").Split('\n').Contains(line))
                {
                    Fail($"baselineAppends {b.File}", $"line missing: {line}");
                }
            }
        }
        foreach (BaselineRemovalEntry b in _m.Spec.BaselineRemovals)
        {
            foreach (string key in b.Keys)
            {
                _checked++;
                if (!_removedKeys.Contains(RepoPaths.Norm(b.File) + "|" + key))
                {
                    Fail($"baselineRemovals {b.File}", $"no line with key '{key}'");
                }
            }
        }
        _report.Info("conformance", $"{_checked} spec rows checked against the final tree");
    }
}
