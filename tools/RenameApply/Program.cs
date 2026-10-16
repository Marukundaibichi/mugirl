using System.Diagnostics;
using RenameApply;
using RenameApply.CSharp;
using RenameApply.Xml;

Console.OutputEncoding = System.Text.Encoding.UTF8;
TextWriter log = Console.Error;
Options opt;
try
{
    opt = Options.Parse(args);
}
catch (ToolException ex)
{
    log.WriteLine(ex.Message);
    return 2;
}

if (opt.RestoreDir != null)
{
    try
    {
        return Transaction.Restore(opt.RestoreDir, log);
    }
    catch (Exception ex) when (ex is ToolException or IOException)
    {
        log.WriteLine(ex.Message);
        return 3;
    }
}

var report = new RunReport { DryRun = opt.DryRun, StartedUtc = DateTimeOffset.UtcNow };
var sw = Stopwatch.StartNew();
try
{
    var repo = new RepoPaths(opt.Repo);
    if (!Directory.Exists(repo.Full("Source")) || !Directory.Exists(repo.Full("1.7")))
    {
        throw new ToolException($"{repo.Root} does not look like the mod root (Source/ and 1.7/ expected)");
    }
    report.Repo = repo.Root;
    MergedSpec spec = MergedSpec.Load(opt.Specs);
    report.Specs.AddRange(spec.Sources);
    report.Waves.AddRange(spec.Waves);
    report.Notes.AddRange(spec.Notes);
    RenameModel model = RenameModel.Build(spec, FrozenNames.Load(opt.FrozenNamesPath), report);
    log.WriteLine($"spec: {model.Types.Count} types, {spec.FileMoves.Count} file moves, {model.Defs.Count} defs, {model.Keyed.Count} keys, {model.AbstractNames.Count} abstract Names, {model.ContentFileMoves.Count} content moves; "
        + $"{spec.MemberRenames.Count} member renames, {spec.MemberMoves.Sum(x => x.Members.Count)} member moves, {spec.MemberDeletes.Sum(x => x.Members.Count)} member deletes, {spec.CreateTypes.Count} created types, "
        + $"{spec.XmlSplits.Count} xml splits, {spec.KeyedSplits.Count} keyed splits, {spec.AttributeArgumentRewrites.Count} attribute rewrites, {spec.TextEdits.Count} text edits");
    if (report.ErrorCount > 0)
    {
        return Finish(report, opt, written: false, exitCode: 2);
    }

    log.WriteLine("C#: loading projects (design-time builds)...");
    var cs = new CSharpPass(repo, model, report, opt, log);
    cs.Load();
    cs.Run();
    log.WriteLine($"C#: done ({sw.Elapsed.TotalSeconds:F0}s)");

    log.WriteLine("XML: 1.7/**...");
    var xml = new XmlPass(repo, model, cs.Tables, cs.CreateDefTypeResolver(), report, cs.CreateXmlSchema(), cs.XmlFieldRenames);
    xml.Run();

    var splits = new XmlSplitPass(repo, model, report);
    if (splits.HasWork)
    {
        log.WriteLine("XML splits...");
        splits.Run();
    }

    var csMoves = cs.FileMoves.Where(kv => !kv.Key.StartsWith("(new)", StringComparison.Ordinal)).Select(kv => (kv.Key, kv.Value)).ToList();
    cs.Tables.SetPaths(csMoves.Concat(xml.Moves));

    var movedFull = cs.Docs.Where(d => d.OrigFull != null && !string.Equals(d.OrigFull, d.OutFull, StringComparison.Ordinal))
        .ToDictionary(d => d.OrigFull!, d => d.OutFull, StringComparer.OrdinalIgnoreCase);
    var proj = new CsprojPass(repo, report, movedFull, cs.ExtractedFiles);
    proj.Run();

    log.WriteLine("baselines...");
    var baselines = new BaselinePass(repo, cs.Tables, report, opt, csMoves.Concat(xml.Moves).ToList(), model.Spec.BaselineAppends,
        report.Maps.Members.Where(m => m.Kind is "rename" or "move").Select(m => (m.Old, m.New)).ToList(), model.Spec.BaselineRemovals);
    baselines.Run();

    var editSites = report.Edits.GroupBy(e => e.File + ":" + e.Line, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Select(e => e.Before).ToList(), StringComparer.OrdinalIgnoreCase);
    // Derived names (Make_/Corpse_/Blueprint_ ...) go into the map only when the def's shape implies them or the run met them.
    DefShapes shapes = DefShapes.Load(repo, opt.EffectiveGameDir);
    if (shapes.Problem != null)
    {
        report.Warn("def-shapes", shapes.Problem);
    }
    var editedTexts = report.Edits.Select(e => e.Before).ToList();
    foreach (DefRename d in model.Defs)
    {
        IReadOnlyList<string>? prefixes = d.IsThingDef ? shapes.DerivedPrefixes(d.Old) : null;
        if (d.IsThingDef && prefixes == null)
        {
            report.Info("derived-shape-unknown", $"ThingDef {d.Old}: shape unknown (not defined in 1.7 or parent unresolved); every derived prefix is mapped");
        }
        foreach (var (o, n) in d.AllNames())
        {
            string? by = null;
            if (o != d.Old)
            {
                string prefix = o.Substring(0, o.Length - d.Old.Length);
                bool seen = editedTexts.Any(b => b.Contains(o, StringComparison.Ordinal));
                if (prefixes != null && !prefixes.Contains(prefix) && !seen)
                {
                    continue;
                }
                by = prefixes == null ? "unknown shape" : prefixes.Contains(prefix) ? "shape" : "referenced";
            }
            report.Maps.Defs.Add(new DefMapEntry
            {
                DefType = d.DefType, RootDefType = o == d.Old ? d.Root : DefRename.DerivedRoot(o),
                Old = o, New = n, Derived = o != d.Old, DerivedBy = by, SaveVisible = d.Entry.SaveVisible, Module = d.Entry.Module,
                NewNameSharedWithOtherDefTypes = d.Entry.NewNameSharedWithOtherDefTypes,
            });
        }
        // A listed site that a type-aware rule renamed (its <defName>, a DefInjected key, a [DefOf] field) counts as used.
        foreach (string r in d.ExplicitRefs.Where(r => !d.UsedExplicitRefs.Contains(r)).ToList())
        {
            if (editSites.TryGetValue(r, out var befores) && d.AllNames().Any(n => befores.Any(b => b.Contains(n.Old, StringComparison.Ordinal))))
            {
                d.UsedExplicitRefs.Add(r);
            }
        }
        foreach (string r in d.NotRefs.Where(r => !d.UsedNotRefs.Contains(r)))
        {
            report.Error("not-ref-unused", $"notRef {r} of {d.DefType} {d.Old} matched no reference (stale line or wrong file)");
        }
        foreach (string r in d.ExplicitRefs.Where(r => !d.UsedExplicitRefs.Contains(r)))
        {
            report.Error("explicit-ref-unused", $"explicitRef {r} of {d.DefType} {d.Old} matched no reference (stale line, wrong file, or not a whole-text/xpath/literal reference)");
        }
    }
    report.Maps.Keyed.AddRange(model.Keyed.Select(kv => new NameMapEntry { Old = kv.Key, New = kv.Value, Module = model.KeyedModule[kv.Key] }));
    report.Maps.AbstractNames.AddRange(model.AbstractNames.Select(kv => new NameMapEntry { Old = kv.Key, New = kv.Value, Module = model.AbstractModule[kv.Key] }));
    report.Maps.Files.AddRange(xml.Moves.Select(m => new NameMapEntry { Old = m.Old, New = m.New }));

    var allWrites = new List<PendingWrite>();
    allWrites.AddRange(cs.Writes.Select(w => new PendingWrite(w.OutFull, w.Text, w.Bom, w.DeleteFull)));
    allWrites.AddRange(cs.Deletes.Select(d => new PendingWrite(d, null, false, d)));
    allWrites.AddRange(xml.Writes);
    allWrites.AddRange(splits.Writes);
    allWrites.AddRange(proj.Writes);
    allWrites.AddRange(baselines.Writes);

    // Text edits on files outside the C# compilation (project files, scripts): applied on top of any pending write.
    foreach (TextEditEntry spec0 in model.Spec.TextEdits.Where(te => !cs.UsedTextEdits.Contains(te)))
    {
        TextEditEntry te = spec0;
        string full = repo.Full(RepoPaths.Norm(te.File));
        int wi = allWrites.FindIndex(w => string.Equals(w.OutFull, full, StringComparison.OrdinalIgnoreCase) && w.Text != null);
        if (wi < 0 && !File.Exists(full))
        {
            report.Error("text-edit-file", $"textEdits: {te.File} does not exist");
            continue;
        }
        TextFileData? data = wi < 0 ? TextFileData.Read(full) : null;
        string text = wi >= 0 ? allWrites[wi].Text! : data!.Text;
        string nl = TextFileData.DetectNewLine(text);
        te = new TextEditEntry
        {
            File = te.File, Module = te.Module, Reason = te.Reason,
            Old = TextFileData.ToNewLine(te.Old, nl),
            New = TextFileData.ToNewLine(te.New, nl),
        };
        int at = text.IndexOf(te.Old, StringComparison.Ordinal);
        if (at < 0 || text.IndexOf(te.Old, at + 1, StringComparison.Ordinal) >= 0)
        {
            report.Error("text-edit-not-unique", $"textEdits: '{te.Old}' must occur exactly once in {te.File}");
            continue;
        }
        string result = text.Substring(0, at) + te.New + text.Substring(at + te.Old.Length);
        var lines = new LineIndex(text);
        report.Edits.Add(new EditRecord { File = RepoPaths.Norm(te.File), Line = lines.Get(at).Line, Column = lines.Get(at).Col, Kind = "text-edit", Before = te.Old, After = te.New });
        if (wi >= 0)
        {
            allWrites[wi] = allWrites[wi] with { Text = result };
        }
        else
        {
            allWrites.Add(new PendingWrite(full, result, data!.Bom, null));
        }
    }

    log.WriteLine("compile order (MSBuild verification)...");
    new CompileOrderPass(repo, report, cs).Run(allWrites);

    log.WriteLine("conformance (applied == spec)...");
    new ConformancePass(repo, model, report, allWrites, baselines.RemovedKeys).Run();

    var audit = new AuditPass(repo, cs.Tables, report, opt, cs.KeyedKeys());
    var editedRel = allWrites.SelectMany(w => new[] { repo.Rel(w.OutFull), w.DeleteFull != null ? repo.Rel(w.DeleteFull) : null }).Where(x => x != null).Select(x => x!).ToHashSet(StringComparer.OrdinalIgnoreCase);
    audit.ScanUnedited(editedRel);
    audit.ScanFinalCSharp(cs.Docs.Where(d => d.Editable).Select(d => (repo.Rel(d.OutFull), d.Text1 ?? d.Text0)));

    // Safety net: nothing outside the editable zones, no duplicate targets.
    foreach (PendingWrite w in allWrites)
    {
        if (!RepoPaths.IsEditable(repo.Rel(w.OutFull)) || (w.DeleteFull != null && !RepoPaths.IsEditable(repo.Rel(w.DeleteFull))))
        {
            report.Error("write-outside-zone", $"refusing to write {repo.Rel(w.OutFull)}");
        }
    }
    foreach (var g in allWrites.GroupBy(w => w.OutFull, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
    {
        report.Error("write-collision", $"several writes target {repo.Rel(g.Key)}");
    }

    if (opt.DumpDir != null)
    {
        foreach (PendingWrite w in allWrites.Where(w => w.Text != null))
        {
            TextFileData.Write(Path.Combine(Path.GetFullPath(opt.DumpDir), repo.Rel(w.OutFull)), w.Text!, w.Bom);
        }
    }
    if (opt.DryRun)
    {
        return Finish(report, opt, written: false, exitCode: report.ErrorCount > 0 ? 1 : 0);
    }
    if (report.ErrorCount > 0 && !opt.Force)
    {
        log.WriteLine($"{report.ErrorCount} errors: nothing written.");
        return Finish(report, opt, written: false, exitCode: 1);
    }
    Transaction.Apply(repo, allWrites, report, opt.BackupDir, opt.KeepBackup, log);
    return Finish(report, opt, written: true, exitCode: report.ErrorCount > 0 ? 1 : 0);
}
catch (ToolException ex)
{
    report.Error("fatal", ex.Message);
    return Finish(report, opt, written: false, exitCode: 3);
}
catch (Exception ex)
{
    report.Error("internal", ex.ToString());
    return Finish(report, opt, written: false, exitCode: 3);
}

static int Finish(RunReport report, Options opt, bool written, int exitCode)
{
    report.Written = written;
    report.Finish();
    if (opt.ReportPath != null)
    {
        report.Save(opt.ReportPath);
    }
    if (opt.AppliedPath != null && (written || opt.DryRun))
    {
        report.SaveApplied(opt.AppliedPath);
    }
    report.Print(Console.Out, full: opt.DryRun || !opt.Quiet);
    Console.Out.WriteLine($"exit {exitCode}; report: {opt.ReportPath}");
    return exitCode;
}
