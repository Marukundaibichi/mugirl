using System.Text.RegularExpressions;
using RenameApply.Xml;

namespace RenameApply;

// Validator baselines (Source/Tests/MugirlRace.Validation/Baselines/*.txt|json, tools/*.json): token replacement in place.
public sealed class BaselinePass(RepoPaths repo, NameTables t, RunReport report, Options opt, IReadOnlyCollection<(string Old, string New)> movedFiles, IReadOnlyList<BaselineAppendEntry>? appends = null, IReadOnlyList<(string Old, string New)>? members = null, IReadOnlyList<BaselineRemovalEntry>? removals = null)
{
    public List<PendingWrite> Writes { get; } = new();
    public HashSet<string> RemovedKeys { get; } = new(StringComparer.Ordinal);   // "rel|key"

    public void Run()
    {
        var files = new List<string>();
        string bl = repo.Full("Source/Tests/MugirlRace.Validation/Baselines");
        if (Directory.Exists(bl))
        {
            files.AddRange(Directory.EnumerateFiles(bl, "*.txt", SearchOption.TopDirectoryOnly));
            files.AddRange(Directory.EnumerateFiles(bl, "*.json", SearchOption.TopDirectoryOnly));
        }
        string tools = repo.Full("tools");
        if (Directory.Exists(tools))
        {
            files.AddRange(Directory.EnumerateFiles(tools, "*.json", SearchOption.TopDirectoryOnly));
        }
        // Directories of moved C# files (Source/MugirlRace-relative), for allowedSources-style prefix warnings.
        var movedDirs = new List<(string OldDir, string File)>();
        foreach (var (o, n) in movedFiles)
        {
            const string main = "Source/MugirlRace/";
            if (!o.StartsWith(main, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string ro = o.Substring(main.Length), rn = n.StartsWith(main, StringComparison.OrdinalIgnoreCase) ? n.Substring(main.Length) : n;
            string[] parts = ro.Split('/');
            for (int i = 1; i < parts.Length; i++)
            {
                string dir = string.Join("/", parts.Take(i)) + "/";
                if (!rn.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                {
                    movedDirs.Add((dir, ro));
                }
            }
        }
        foreach (string full in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            string rel = repo.Rel(full);
            bool reportOnly = opt.ReportOnlyBaselines.Any(g => Glob.IsMatch(g, rel));
            TextFileData data = TextFileData.Read(full);
            var lines = new LineIndex(data.Text);
            var edits = new List<TextEdit>();
            var taken = new List<(int S, int E)>();
            void Add(Match mt, string newText, string kind)
            {
                if (taken.Any(x => mt.Index < x.E && mt.Index + mt.Length > x.S))
                {
                    return;
                }
                taken.Add((mt.Index, mt.Index + mt.Length));
                int line = lines.Get(mt.Index).Line;
                if (reportOnly)
                {
                    report.Warn("baseline-report-only", $"{mt.Value} -> {newText} not applied (report-only baseline: decide by hand)", rel, line, lines.LineText(line).Trim());
                    return;
                }
                edits.Add(new TextEdit(mt.Index, mt.Length, newText, kind));
            }
            string text = data.Text;
            var removeKeys = (removals ?? Array.Empty<BaselineRemovalEntry>()).Where(r => string.Equals(RepoPaths.Norm(r.File), rel, StringComparison.OrdinalIgnoreCase))
                .SelectMany(r => r.Keys).ToHashSet(StringComparer.Ordinal);
            for (int pos = 0; removeKeys.Count > 0 && pos < text.Length;)
            {
                int end = text.IndexOf('\n', pos);
                int next = end < 0 ? text.Length : end + 1;
                string key = text.Substring(pos, (end < 0 ? text.Length : end) - pos).TrimEnd('\r').Split('\t')[0].Trim();
                if (removeKeys.Contains(key))
                {
                    edits.Add(new TextEdit(pos, next - pos, "", "baseline-remove"));
                    taken.Add((pos, next));
                    RemovedKeys.Add(rel + "|" + key);
                }
                pos = next;
            }
            if (t.TypeFullRx != null)
            {
                foreach (Match mt in t.TypeFullRx.Matches(text))
                {
                    Add(mt, t.TypeFull[mt.Value], "baseline-type");
                }
            }
            if (t.KeyRx != null)
            {
                foreach (Match mt in t.KeyRx.Matches(text))
                {
                    Add(mt, t.Keys[mt.Value], "baseline-key");
                }
            }
            if (t.PathRx != null)
            {
                foreach (Match mt in t.PathRx.Matches(text))
                {
                    Add(mt, t.MapPath(mt.Value), "baseline-path");
                }
            }
            if (t.DefRx != null)
            {
                foreach (Match mt in t.DefRx.Matches(text))
                {
                    int line = lines.Get(mt.Index).Line;
                    string refKey = rel + ":" + line;
                    if (t.IsNotRef(mt.Value, refKey))
                    {
                        continue;
                    }
                    var entries = t.DefByOld[mt.Value];
                    var hits = entries.Where(x => x.Def.ExplicitRefs.Contains(refKey)).ToList();
                    string? target = hits.Count == 1 ? hits[0].New : t.UniqueDefTarget(mt.Value, out _);
                    if (hits.Count == 1)
                    {
                        hits[0].Def.UsedExplicitRefs.Add(refKey);
                    }
                    if (target != null)
                    {
                        Add(mt, target, "baseline-def");
                    }
                    else
                    {
                        report.Warn("baseline-def-shared", $"{mt.Value} is shared by several def types; not replaced (add {refKey} to explicitRefs)", rel, line, lines.LineText(line).Trim());
                    }
                }
            }
            if (t.AbstractRx != null)
            {
                foreach (Match mt in t.AbstractRx.Matches(text))
                {
                    Add(mt, t.Abstract[mt.Value], "baseline-abstract");
                }
            }
            foreach (var (dir, file) in movedDirs.DistinctBy(x => x.OldDir))
            {
                foreach (Match mt in Regex.Matches(text, "[\"'\\s]" + Regex.Escape(dir) + "[\"'\\s]"))
                {
                    int line = lines.Get(mt.Index).Line;
                    report.Warn("baseline-path-prefix", $"folder {dir} allows files that moved out of it (e.g. {file}); review the rule", rel, line, lines.LineText(line).Trim());
                }
            }
            // Member moves/renames: "Old.Type::Member" and "Old.Type.Member" tokens (architecture rules, allow lists).
            foreach (var (mo, mn) in members ?? Array.Empty<(string, string)>())
            {
                int dot = mo.LastIndexOf('.'), ndot = mn.LastIndexOf('.');
                foreach (var (o, n) in new[] { (mo.Substring(0, dot) + "::" + mo.Substring(dot + 1), mn.Substring(0, ndot) + "::" + mn.Substring(ndot + 1)), (mo, mn) })
                {
                    foreach (Match mt in Regex.Matches(text, @"(?<![\w.])" + Regex.Escape(o) + @"(?![\w])"))
                    {
                        Add(mt, n, "baseline-member");
                    }
                }
            }
            foreach (BaselineAppendEntry a in (appends ?? Array.Empty<BaselineAppendEntry>()).Where(a => string.Equals(RepoPaths.Norm(a.File), rel, StringComparison.OrdinalIgnoreCase)))
            {
                string nl = data.NewLine;
                string add = string.Concat(a.Lines.Select(l => l + nl));
                string prefix = text.Length > 0 && !text.EndsWith('\n') ? nl : "";
                edits.Add(new TextEdit(text.Length, 0, prefix + add, "baseline-append"));
            }
            if (edits.Count == 0)
            {
                continue;
            }
            var norm = TextEdits.Normalize(edits, rel);
            foreach (TextEdit e in norm)
            {
                var (line, col) = lines.Get(e.Start);
                report.Edits.Add(new EditRecord
                {
                    File = rel, Line = line, Column = col, Kind = e.Kind, Before = text.Substring(e.Start, e.Length), After = e.NewText,
                    BeforeLine = lines.LineText(line).Trim(),
                });
            }
            Writes.Add(new PendingWrite(full, TextEdits.Apply(text, norm), data.Bom, null));
        }
    }
}

// Project files: Compile Include/Remove/Update paths that point at moved C# files (e.g. NegativeControl's linked sources).
public sealed class CsprojPass(RepoPaths repo, RunReport report, IReadOnlyDictionary<string, string> movedFullPaths, IReadOnlyDictionary<string, List<string>>? extracted = null)
{
    public List<PendingWrite> Writes { get; } = new();

    public void Run()
    {
        foreach (string proj in Directory.EnumerateFiles(repo.Full("Source"), "*.*proj", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(repo.Full("Source"), "*.props", SearchOption.AllDirectories))
                     .Concat(Directory.EnumerateFiles(repo.Full("Source"), "*.targets", SearchOption.AllDirectories))
                     .Where(p => !RepoPaths.HasSegment(repo.Rel(p), "bin") && !RepoPaths.HasSegment(repo.Rel(p), "obj"))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            string rel = repo.Rel(proj);
            TextFileData data = TextFileData.Read(proj);
            XDoc doc;
            try
            {
                doc = XDoc.Parse(data.Text);
            }
            catch (FormatException ex)
            {
                report.Error("project-parse", ex.Message, rel);
                continue;
            }
            string dir = Path.GetDirectoryName(proj)!;
            var lines = new LineIndex(data.Text);
            var edits = new List<TextEdit>();
            foreach (XElem e in doc.AllElements())
            {
                foreach (XAttr a in e.Attrs.Where(a => a.Name is "Include" or "Remove" or "Update"))
                {
                    string v = a.Value;
                    if (v.Contains('*') || v.Contains('$') || v.Contains(';') || !v.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    string full = Path.GetFullPath(Path.Combine(dir, v.Replace('/', Path.DirectorySeparatorChar)));
                    bool slash = v.Contains('/') && !v.Contains('\\');
                    string Rel(string target)
                    {
                        string r = Path.GetRelativePath(dir, target);
                        return slash ? r.Replace('\\', '/') : r.Replace('/', '\\');
                    }
                    // A linked file whose types were extracted: link the new files next to it (same Link folder).
                    if (a.Name == "Include" && e.Name == "Compile" && extracted != null && extracted.TryGetValue(full, out var created))
                    {
                        int lineStart = data.Text.LastIndexOf('\n', e.Start) + 1;
                        string indent = data.Text.Substring(lineStart, e.Start - lineStart);
                        string nl = TextFileData.DetectNewLine(data.Text);
                        string? link = e.Attr("Link")?.Value;
                        var sb = new System.Text.StringBuilder();
                        foreach (string c in created.OrderBy(x => x, StringComparer.Ordinal))
                        {
                            string linkAttr = link == null ? "" : $" Link=\"{Path.Combine(Path.GetDirectoryName(link) ?? "", Path.GetFileName(c))}\"";
                            sb.Append(nl).Append(indent).Append($"<Compile Include=\"{Rel(c)}\"{linkAttr} />");
                        }
                        edits.Add(new TextEdit(e.End, 0, sb.ToString(), "project-link-extracted"));
                    }
                    if (!movedFullPaths.TryGetValue(full, out string? newFull))
                    {
                        continue;
                    }
                    edits.Add(new TextEdit(a.ValueStart, a.ValueLength, Rel(newFull), "project-path"));
                }
            }
            if (edits.Count == 0)
            {
                continue;
            }
            foreach (TextEdit e in edits)
            {
                var (line, col) = lines.Get(e.Start);
                report.Edits.Add(new EditRecord { File = rel, Line = line, Column = col, Kind = e.Kind, Before = data.Text.Substring(e.Start, e.Length), After = e.NewText, BeforeLine = lines.LineText(line).Trim() });
            }
            Writes.Add(new PendingWrite(proj, TextEdits.Apply(data.Text, TextEdits.Normalize(edits, rel)), data.Bom, null));
        }
    }
}

// Report-only scan of everything the tool does not edit (docs, README, scripts, fixtures, LoadFolders/About), and residual
// old names in the final C# text.
public sealed class AuditPass(RepoPaths repo, NameTables t, RunReport report, Options opt, IReadOnlySet<string>? keyedKeys = null)
{
    // The whole dotted token around a match ("Mugirl.CorporateUI.Title" around "Mugirl.CorporateUI").
    private static string TokenAround(string text, int index, int length)
    {
        int s = index, e = index + length;
        while (s > 0 && (char.IsLetterOrDigit(text[s - 1]) || text[s - 1] is '_' or '.'))
        {
            s--;
        }
        while (e < text.Length && (char.IsLetterOrDigit(text[e]) || text[e] is '_' or '.'))
        {
            e++;
        }
        return text.Substring(s, e - s).Trim('.');
    }

    private static readonly string[] TextExtensions = { ".md", ".ps1", ".psm1", ".cs", ".txt", ".json", ".xml", ".props", ".targets", ".csproj", ".yml", ".yaml", ".editorconfig" };

    public void ScanUnedited(ISet<string> editedRel)
    {
        var roots = new List<string> { "docs", "tools", "About", "Source" };
        var files = new List<string>();
        foreach (string r in roots)
        {
            string full = repo.Full(r);
            if (Directory.Exists(full))
            {
                files.AddRange(Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories));
            }
        }
        foreach (string top in new[] { "README.md", "AGENTS.md", "LoadFolders.xml" })
        {
            if (File.Exists(repo.Full(top)))
            {
                files.Add(repo.Full(top));
            }
        }
        var perFile = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string full in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            string rel = repo.Rel(full);
            string ext = Path.GetExtension(full).ToLowerInvariant();
            if (!TextExtensions.Contains(ext) || RepoPaths.HasSegment(rel, "bin") || RepoPaths.HasSegment(rel, "obj") || editedRel.Contains(rel))
            {
                continue;
            }
            // C# sources and project files of the build are handled by the C# pass; baselines by the baseline pass.
            bool isBuildSource = rel.StartsWith("Source/", StringComparison.OrdinalIgnoreCase) && (ext == ".cs" || ext.EndsWith("proj", StringComparison.Ordinal) || ext is ".props" or ".targets");
            bool isBaseline = rel.StartsWith("Source/Tests/MugirlRace.Validation/Baselines/", StringComparison.OrdinalIgnoreCase) && rel.Count(c => c == '/') == 4
                              || (rel.StartsWith("tools/", StringComparison.OrdinalIgnoreCase) && rel.Count(c => c == '/') == 1 && ext == ".json");
            bool isLegacyCorpus = rel.StartsWith("Source/Tests/MugirlRace.Validation/Baselines/legacy/", StringComparison.OrdinalIgnoreCase);
            if (isBuildSource || isBaseline || isLegacyCorpus)
            {
                continue;
            }
            string text;
            try
            {
                text = TextFileData.Read(full).Text;
            }
            catch (ToolException)
            {
                continue;
            }
            var lines = new LineIndex(text);
            int count = 0;
            foreach (var (rx, what) in new[] { (t.TypeFullRx, "type"), (t.DistinctiveShortRx, "short type"), (t.DefRx, "defName"), (t.KeyRx, "Keyed key"), (t.AbstractRx, "abstract Name"), (t.PathRx, "moved path") })
            {
                if (rx == null)
                {
                    continue;
                }
                foreach (Match mt in rx.Matches(text))
                {
                    int line = lines.Get(mt.Index).Line;
                    count++;
                    if (rel.StartsWith("docs/", StringComparison.OrdinalIgnoreCase))
                    {
                        report.Info("docs-mention", $"{what} {mt.Value} (docs are rewritten in Wave D)", rel, line);
                    }
                    else
                    {
                        report.Warn("unedited-mention", $"{what} {mt.Value} in a file the tool does not edit", rel, line, lines.LineText(line).Trim());
                    }
                }
            }
            if (count > 0)
            {
                perFile[rel] = count;
            }
        }
        foreach (var kv in perFile.Where(kv => kv.Key.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)))
        {
            report.Notes.Add($"docs mentions: {kv.Value,4}  {kv.Key}");
        }
    }

    public void ScanFinalCSharp(IEnumerable<(string Rel, string Text)> files)
    {
        foreach (var (rel, text) in files)
        {
            if (opt.LiteralExcludes.Any(g => Glob.IsMatch(g, rel)))
            {
                continue;
            }
            // PublicApi files name the frozen public types on purpose.
            bool publicApi = rel.StartsWith("Source/MugirlRace/PublicApi/", StringComparison.OrdinalIgnoreCase);
            var lines = new LineIndex(text);
            if (t.TypeFullRx != null)
            {
                foreach (Match mt in t.TypeFullRx.Matches(text))
                {
                    int line = lines.Get(mt.Index).Line;
                    if (keyedKeys != null && keyedKeys.Contains(TokenAround(text, mt.Index, mt.Length)))
                    {
                        report.Info("csharp-residual-keyed-key", $"Keyed key shares the prefix of old type name {mt.Value} (keys are not types)", rel, line);
                        continue;
                    }
                    if (publicApi)
                    {
                        report.Info("csharp-residual-public-api", $"public API file names {mt.Value} on purpose", rel, line);
                        continue;
                    }
                    report.Warn("csharp-residual-type", $"old type name {mt.Value} remains", rel, line, lines.LineText(line).Trim());
                }
            }
            if (t.TypeShortRx != null)
            {
                foreach (Match mt in t.TypeShortRx.Matches(text))
                {
                    int line = lines.Get(mt.Index).Line;
                    report.Info("csharp-residual-short", $"old short name {mt.Value} remains (literal, comment or unrelated symbol)", rel, line, lines.LineText(line).Trim());
                }
            }
        }
    }
}
