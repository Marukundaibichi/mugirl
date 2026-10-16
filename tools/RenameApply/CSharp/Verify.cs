using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RenameApply.CSharp;

// Requirements -> usings, rendering, compile/binding verification with deterministic fixes, using cleanup, writes.
public sealed partial class CSharpPass
{
    private static readonly HashSet<string> QualifyFixIds = new(StringComparer.Ordinal)
    {
        "CS0104", // ambiguous reference
        "CS0246", // type or namespace not found
        "CS0103", // name does not exist
        "CS0234", // not found in namespace
        "CS0118", // namespace used like a type / type used like a variable
        "CS0119", // not valid in the given context
        "CS0426", // type name does not exist in the type
        "CS0576", // conflicts with alias
        "CS0436", // conflicts with imported type
    };

    private CompilationUnitSyntax RootFor(DocPlan plan)
    {
        ProjectModel p = plan.Doc.Projects.Contains(Main) ? Main : plan.Doc.Projects[0];
        return _docTree0[(p, plan.Doc)].GetCompilationUnitRoot();
    }

    private void EvaluateRequirements()
    {
        foreach (DocPlan plan in _plans.Values)
        {
            CompilationUnitSyntax root = RootFor(plan);
            var cuUsings = plan.Usings.Where(u => u.ResolvedNamespace != null).Select(u => u.ResolvedNamespace!).ToHashSet(StringComparer.Ordinal);
            foreach (Requirement r in plan.Requirements)
            {
                if (r.Site.Removed)
                {
                    continue;
                }
                List<string> chain = NewChainAt(plan, root, r.Site.Start);
                if (chain.Contains(r.Namespace) || cuUsings.Contains(r.Namespace) || _globalUsings[r.Project].Contains(r.Namespace))
                {
                    continue;
                }
                plan.AddUsings.Add(r.Namespace);
            }
            foreach (string ns in plan.ExtraNamespaces)
            {
                List<string> chain0 = NewChainAt(plan, root, root.Members.FirstOrDefault()?.SpanStart ?? 0);
                if (!chain0.Contains(ns) && !cuUsings.Contains(ns) && !plan.Doc.Projects.Any(p => _globalUsings[p].Contains(ns)))
                {
                    plan.AddUsings.Add(ns);
                }
            }
            foreach (Site s in plan.Sites.Values.Where(s => s.Kind == SiteKind.NamespaceLeft && !s.Removed))
            {
                List<string> chain = NewChainAt(plan, root, s.Start);
                string id = s.OrigText.TrimStart('@');
                string? resolved = chain.Select(e => e.Length == 0 ? id : e + "." + id).FirstOrDefault(_newNamespaces.Contains);
                if (resolved == s.BoundNamespace)
                {
                    continue;
                }
                if (!_newNamespaces.Contains(s.BoundNamespace!))
                {
                    _report.Error("namespace-vanished", $"'{s.OrigText}' refers to namespace {s.BoundNamespace}, which no longer exists after the moves", RelOf(plan.Doc), LinesOf(plan.Doc).Get(s.Start).Line);
                    continue;
                }
                s.Qualify = true;
                s.EditKind = "namespace-ref-qualified";
            }
        }
    }

    private static int CompareUsing(string a, string b)
    {
        bool sa = a == "System" || a.StartsWith("System.", StringComparison.Ordinal);
        bool sb = b == "System" || b.StartsWith("System.", StringComparison.Ordinal);
        if (sa != sb)
        {
            return sa ? -1 : 1;
        }
        int c = StringComparer.OrdinalIgnoreCase.Compare(a, b);
        return c != 0 ? c : StringComparer.Ordinal.Compare(a, b);
    }

    private List<TextEdit> UsingEdits(DocPlan plan)
    {
        var edits = new List<TextEdit>();
        string nl = plan.Doc.NewLine;
        string text = plan.Doc.Text0;
        var cu = plan.Usings.Where(u => !u.InsideNamespace).OrderBy(u => u.Start).ToList();
        var kept = cu.Where(u => !plan.RemoveUsings.Contains(u.Start)).ToList();
        var present = kept.Select(u => u.ResolvedNamespace ?? u.NamespaceName).ToHashSet(StringComparer.Ordinal);
        var add = plan.AddUsings.Where(ns => !present.Contains(ns)).OrderBy(ns => ns, Comparer<string>.Create(CompareUsing)).ToList();
        var plain = kept.Where(u => !u.NamespaceName.StartsWith('#')).ToList();
        var removedPlain = cu.Where(u => plan.RemoveUsings.Contains(u.Start) && !u.NamespaceName.StartsWith('#')).ToList();
        if (add.Count > 0)
        {
            if (plain.Count == 0 && removedPlain.Count > 0)
            {
                // Every original using goes away: the new ones take the place of the first (its blank separator stays).
                edits.Add(new TextEdit(removedPlain[0].LineStart, 0, string.Concat(add.Select(ns => "using " + ns + ";" + nl)), "using-add"));
            }
            else if (plain.Count > 0)
            {
                foreach (string ns in add)
                {
                    UsingInfo? after = plain.FirstOrDefault(u => CompareUsing(ns, u.NamespaceName) < 0);
                    int pos = after?.LineStart ?? plain[^1].LineEnd;
                    string prefix = after == null && pos == text.Length && !text.EndsWith('\n') ? nl : "";
                    edits.Add(new TextEdit(pos, 0, prefix + "using " + ns + ";" + nl, "using-add"));
                }
            }
            else if (kept.Count > 0)
            {
                edits.Add(new TextEdit(kept[0].LineStart, 0, string.Concat(add.Select(ns => "using " + ns + ";" + nl)), "using-add"));
            }
            else
            {
                CompilationUnitSyntax root = RootFor(plan);
                var first = root.AttributeLists.Cast<SyntaxNode>().Concat(root.Members).OrderBy(n => n.SpanStart).FirstOrDefault();
                int pos = first == null ? 0 : root.SyntaxTree.GetText().Lines.GetLineFromPosition(first.SpanStart).Start;
                edits.Add(new TextEdit(pos, 0, string.Concat(add.Select(ns => "using " + ns + ";" + nl)) + nl, "using-add"));
            }
        }
        var removed = cu.Where(u => plan.RemoveUsings.Contains(u.Start)).ToList();
        foreach (UsingInfo u in removed)
        {
            int end = u.LineEnd;
            if (kept.Count == 0 && add.Count == 0 && u == removed[^1])
            {
                // Last using of the file gone: also drop the blank separator line that followed the using block.
                int e = end;
                while (e < text.Length && (text[e] == ' ' || text[e] == '\t'))
                {
                    e++;
                }
                if (e < text.Length && text[e] == '\n')
                {
                    end = e + 1;
                }
                else if (e + 1 < text.Length && text[e] == '\r' && text[e + 1] == '\n')
                {
                    end = e + 2;
                }
            }
            edits.Add(new TextEdit(u.LineStart, end - u.LineStart, "", "using-remove"));
        }
        return edits;
    }

    private void RenderAll()
    {
        var pending = new Dictionary<DocPlan, List<TextEdit>>();
        foreach (DocPlan plan in _plans.Values)
        {
            var edits = new List<TextEdit>();
            foreach (Site s in plan.Sites.Values)
            {
                if (!s.Removed && s.IsEdit)
                {
                    edits.Add(new TextEdit(s.ReplaceStart, s.ReplaceLength, s.EffectiveText, s.Qualify ? (s.EditKind == "namespace-ref-qualified" ? s.EditKind : "qualify-ambiguous") : s.EditKind ?? "rename"));
                }
            }
            edits.AddRange(plan.TriviaEdits);
            foreach (var (start, length, newName, _) in plan.NamespaceDecls)
            {
                edits.Add(new TextEdit(start, length, newName, "namespace-decl"));
            }
            edits.AddRange(UsingEdits(plan));
            // R2: edits inside moved declarations travel with them; the declarations (and dissolved types) are cut out.
            pending[plan] = ExtractMovedSpans(plan, edits);
        }
        foreach (TargetType tt in _targetTypes.Values.Where(t => !t.Created && t.Incoming.Count > 0))
        {
            if (tt.Doc != null && _plans.TryGetValue(tt.Doc, out DocPlan? tp))
            {
                pending[tp].Add(new TextEdit(tt.InsertPos, 0, tt.Doc.NewLine + InsertionText(tt, tt.Doc.NewLine), "member-move-in"));
            }
        }
        foreach (var (plan, edits) in pending)
        {
            plan.AppliedEdits = TextEdits.Normalize(edits, RelOf(plan.Doc));
            plan.Doc.Text1 = TextEdits.Apply(plan.Doc.Text0, plan.AppliedEdits);
        }
        foreach (TargetType tt in _targetTypes.Values.Where(t => t.Created))
        {
            tt.Doc!.Text1 = RenderCreatedType(tt);
        }
    }

    private int NewStartOf(DocPlan plan, Site s) =>
        s.IsEdit ? TextEdits.MapPosition(plan.AppliedEdits!, s.ReplaceStart) : TextEdits.MapPosition(plan.AppliedEdits!, s.Start);

    private int NewAnchorOf(DocPlan plan, Site s) =>
        s.IsEdit ? NewStartOf(plan, s) + s.EffectiveAnchor : NewStartOf(plan, s);

    private Site? SiteAtNewPos(DocPlan plan, int pos)
    {
        foreach (Site s in plan.Sites.Values)
        {
            if (s.Removed)
            {
                continue;
            }
            int start = NewStartOf(plan, s);
            int len = s.IsEdit ? s.EffectiveText.Length : s.OrigText.Length;
            if (pos >= start && pos < start + len)
            {
                return s;
            }
        }
        return null;
    }

    private List<(ProjectModel P, Diagnostic D)> NewErrors(Dictionary<ProjectModel, CSharpCompilation> comps)
    {
        var result = new List<(ProjectModel, Diagnostic)>();
        foreach (ProjectModel p in Projects)
        {
            var baseline = _baselineErrors[p].Select(s => _m.MapDocString(s, Foreign(p))).GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
            foreach (Diagnostic d in comps[p].GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                string sig = ErrorSignature(p, d);
                if (baseline.TryGetValue(sig, out int n) && n > 0)
                {
                    baseline[sig] = n - 1;
                }
                else
                {
                    result.Add((p, d));
                }
            }
        }
        return result;
    }

    private Dictionary<ProjectModel, CSharpCompilation> _final = new();
    private Dictionary<SyntaxTree, CsDoc> _treeDocFinal = new();
    private Dictionary<(ProjectModel, CsDoc), SyntaxTree> _docTreeFinal = new();

    private void CompileFinal()
    {
        _final = BuildAll(final: true, out _treeDocFinal, out _docTreeFinal);
    }

    private void VerifyLoop()
    {
        if (_report.ErrorCount > 0)
        {
            _report.Error("csharp-skipped", "C# rewrite not attempted because of earlier errors");
            VerificationFailed = true;
            RenderAll();
            return;
        }
        for (int iter = 1; iter <= 12; iter++)
        {
            _log.WriteLine($"  verify iteration {iter}: render + compile...");
            RenderAll();
            CompileFinal();
            var errors = NewErrors(_final);
            if (errors.Count == 0)
            {
                _log.WriteLine($"  verify iteration {iter}: binding check...");
                if (!BindingCheck())
                {
                    RecordCompileResults(errors);
                    return;
                }
                continue;
            }
            _log.WriteLine($"  verify iteration {iter}: {errors.Count} new errors, fixing...");
            if (!FixErrors(errors))
            {
                RecordCompileResults(errors);
                VerificationFailed = true;
                _report.Error("csharp-verification", $"{errors.Count} new compile errors could not be fixed mechanically (see compile section)");
                return;
            }
        }
        VerificationFailed = true;
        _report.Error("csharp-verification", "fix loop did not converge");
        RecordCompileResults(NewErrors(_final));
    }

    private void RecordCompileResults(List<(ProjectModel P, Diagnostic D)> errors)
    {
        foreach (ProjectCompileResult r in _report.Compile)
        {
            ProjectModel p = Projects.First(x => x.Name == r.Project);
            r.FinalErrors = _final.TryGetValue(p, out CSharpCompilation? c) ? c.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error) : -1;
            r.NewErrors = errors.Where(e => e.P == p).Select(e => ErrorDisplay(e.D, t => _repo.Rel(t.FilePath))).ToList();
        }
    }

    private bool FixErrors(List<(ProjectModel P, Diagnostic D)> errors)
    {
        bool fixedAny = false;
        foreach (var (p, d) in errors)
        {
            if (!d.Location.IsInSource || !_treeDocFinal.TryGetValue(d.Location.SourceTree!, out CsDoc? doc) || !_plans.TryGetValue(doc, out DocPlan? plan))
            {
                continue;
            }
            SyntaxNode root = d.Location.SourceTree!.GetRoot();
            SyntaxNode node = root.FindNode(d.Location.SourceSpan, getInnermostNodeForTie: true);
            UsingDirectiveSyntax? u = node.FirstAncestorOrSelf<UsingDirectiveSyntax>();
            if (u != null)
            {
                if (u.Name != null && u.Alias == null && u.StaticKeyword.IsKind(SyntaxKind.None) && d.Id is "CS0246" or "CS0234" or "CS0138")
                {
                    string name = u.Name.ToString();
                    UsingInfo? info = plan.Usings.FirstOrDefault(x => x.NamespaceName == name && !plan.RemoveUsings.Contains(x.Start));
                    if (info != null)
                    {
                        plan.RemoveUsings.Add(info.Start);
                        _report.Info("using-removed-vanished", $"removed 'using {name};' (namespace no longer exists)", RelOf(doc));
                        fixedAny = true;
                    }
                    else if (plan.AddUsings.Remove(name))
                    {
                        fixedAny = true;
                    }
                }
                continue;
            }
            if (!QualifyFixIds.Contains(d.Id))
            {
                continue;
            }
            Site? s = SiteAtNewPos(plan, d.Location.SourceSpan.Start);
            if (s != null && !s.Qualify && s.QualifiedText != null)
            {
                s.Qualify = true;
                fixedAny = true;
                _report.Info("qualified", $"{d.Id}: qualified '{s.OrigText}' as {s.EffectiveText}", RelOf(doc), LinesOf(doc).Get(s.Start).Line);
            }
            else if (s != null && s.Qualify && !s.QualifyNeedsGlobal && s.QualifiedText != null)
            {
                s.QualifyNeedsGlobal = true;
                fixedAny = true;
            }
        }
        return fixedAny;
    }

    // Every name must bind to the (mapped) symbol it bound to before the rewrite. Returns true when a fix was applied.
    private bool BindingCheck()
    {
        bool changed = false;
        int checkedCount = 0, mismatches = 0;
        var unresolved = new List<(string Message, string File, int Line, string Text)>();
        foreach (ProjectModel p in Projects)
        {
            foreach (CsDoc d in DocsOf(p).Where(d => d.Editable && _plans.ContainsKey(d)))
            {
                DocPlan plan = _plans[d];
                SyntaxTree tree = _docTreeFinal[(p, d)];
                SemanticModel model = _final[p].GetSemanticModel(tree);
                SyntaxNode root = tree.GetRoot();
                foreach (Site s in plan.Sites.Values)
                {
                    if (s.Removed || s.ExpectedKey == null || s.UnresolvedBaselineProjects.Contains(p))
                    {
                        continue;
                    }
                    MovedMember? movedHere = MovedSpanAt(d, s.Start);
                    if (InDissolvedSpan(d, s.Start) && movedHere == null || movedHere?.Kind == "delete")
                    {
                        continue;
                    }
                    if (movedHere != null)
                    {
                        foreach (var (targetDoc, targetPos) in MovedSitePositions(movedHere, s))
                        {
                            if (!_docTreeFinal.TryGetValue((p, targetDoc), out SyntaxTree? ttree))
                            {
                                continue;
                            }
                            SyntaxToken ttok = ttree.GetRoot().FindToken(targetPos, findInsideTrivia: true);
                            if (ttok.SpanStart != targetPos || ttok.Parent is not SimpleNameSyntax tname)
                            {
                                _report.Warn("binding-check-misaligned", $"internal: no name at the moved position for '{s.OrigText}'", RelOf(d), LinesOf(d).Get(s.Start).Line);
                                continue;
                            }
                            checkedCount++;
                            ISymbol? tsym = BindStrict(_final[p].GetSemanticModel(ttree), tname);
                            if (tsym is IMethodSymbol { MethodKind: MethodKind.Constructor } tctor && IsAttributeName(tname))
                            {
                                tsym = tctor.ContainingType;
                            }
                            string? tkey = KeyOf(tsym);
                            string expected = s.ExpectedKey;
                            if (tkey != expected)
                            {
                                mismatches++;
                                unresolved.Add(($"[{p.Name}] moved '{s.OrigText}' bound to {expected} expected, now {tkey ?? "nothing"}", RelOf(d), LinesOf(d).Get(s.Start).Line, LinesOf(d).LineText(LinesOf(d).Get(s.Start).Line).Trim()));
                            }
                        }
                        continue;
                    }
                    int pos = NewAnchorOf(plan, s);
                    SyntaxToken tok = root.FindToken(pos, findInsideTrivia: true);
                    if (tok.SpanStart != pos || tok.Parent is not SimpleNameSyntax name)
                    {
                        _report.Warn("binding-check-misaligned", $"internal: no name at mapped position for '{s.OrigText}'", RelOf(d), LinesOf(d).Get(s.Start).Line);
                        continue;
                    }
                    ISymbol? sym = BindStrict(model, name);
                    if (sym is IMethodSymbol { MethodKind: MethodKind.Constructor } ctor && IsAttributeName(name))
                    {
                        sym = ctor.ContainingType;
                    }
                    checkedCount++;
                    string? key = KeyOf(sym);
                    if (key == s.ExpectedKey)
                    {
                        continue;
                    }
                    if (s.QualifiedText != null && !s.Qualify)
                    {
                        s.Qualify = true;
                        changed = true;
                        _report.Info("qualified", $"binding changed ({key} instead of {s.ExpectedKey}): qualified '{s.OrigText}'", RelOf(d), LinesOf(d).Get(s.Start).Line);
                        continue;
                    }
                    mismatches++;
                    unresolved.Add(($"[{p.Name}] '{s.OrigText}' bound to {s.ExpectedKey} before, now {key ?? "nothing"}", RelOf(d), LinesOf(d).Get(s.Start).Line, LinesOf(d).LineText(LinesOf(d).Get(s.Start).Line).Trim()));
                }
            }
        }
        _log.WriteLine($"  binding check: {checkedCount} names, {mismatches} mismatches{(changed ? ", re-qualifying" : "")}");
        // Mismatches that depend on a qualified container (member names in crefs, member access) are re-checked after the fix.
        if (!changed && mismatches > 0)
        {
            VerificationFailed = true;
            foreach (var (msg, file, line, text) in unresolved)
            {
                _report.Error("binding-changed", msg, file, line, text);
            }
        }
        return changed;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Unused usings
    // ------------------------------------------------------------------------------------------------------------------

    private Dictionary<CsDoc, HashSet<string>> UnusedUsings(Dictionary<ProjectModel, CSharpCompilation> comps, Dictionary<(ProjectModel, CsDoc), SyntaxTree> docTree)
    {
        var perDoc = new Dictionary<CsDoc, HashSet<string>>();
        foreach (ProjectModel p in Projects)
        {
            var byTree = comps[p].GetDiagnostics().Where(x => x.Id == "CS8019" && x.Location.IsInSource).GroupBy(x => x.Location.SourceTree!).ToDictionary(g => g.Key, g => g.ToList());
            foreach (CsDoc d in DocsOf(p).Where(d => d.Editable && !d.FinalOnly))
            {
                SyntaxTree tree = docTree[(p, d)];
                var set = new HashSet<string>(StringComparer.Ordinal);
                if (byTree.TryGetValue(tree, out var diags))
                {
                    SyntaxNode root = tree.GetRoot();
                    foreach (Diagnostic x in diags)
                    {
                        UsingDirectiveSyntax? u = root.FindNode(x.Location.SourceSpan).FirstAncestorOrSelf<UsingDirectiveSyntax>();
                        if (u?.Name != null && u.Alias == null && u.StaticKeyword.IsKind(SyntaxKind.None) && u.GlobalKeyword.IsKind(SyntaxKind.None))
                        {
                            set.Add(u.Name.ToString());
                        }
                    }
                }
                // A using is unused for the file only if it is unused in every project that compiles the file.
                if (perDoc.TryGetValue(d, out HashSet<string>? prev))
                {
                    prev.IntersectWith(set);
                }
                else
                {
                    perDoc[d] = set;
                }
            }
        }
        return perDoc;
    }

    private void CollectBaselineUnusedUsings()
    {
        foreach (var kv in UnusedUsings(Comp0, _docTree0))
        {
            _baselineUnusedUsings[kv.Key] = kv.Value;
        }
    }

    private static bool IsModNamespace(string ns) => ns == "Mugirl" || ns.StartsWith("Mugirl.", StringComparison.Ordinal);

    private void CleanupUsings()
    {
        var unused = UnusedUsings(_final, _docTreeFinal);
        var removedNow = new List<(DocPlan Plan, int Start, string Name)>();
        foreach (var (d, names) in unused)
        {
            if (!_plans.TryGetValue(d, out DocPlan? plan))
            {
                continue;
            }
            foreach (string name in names)
            {
                if (plan.AddUsings.Contains(name))
                {
                    plan.AddUsings.Remove(name);
                    removedNow.Add((plan, -1, name));
                    continue;
                }
                UsingInfo? u = plan.Usings.FirstOrDefault(x => x.NamespaceName == name && !x.InsideNamespace && !plan.RemoveUsings.Contains(x.Start));
                if (u == null)
                {
                    continue;
                }
                bool wasUnused = _baselineUnusedUsings.TryGetValue(d, out HashSet<string>? before) && before.Contains(name);
                bool eligible = d.Created || (!wasUnused && (d.Extracted || IsModNamespace(u.ResolvedNamespace ?? name)));
                if (eligible)
                {
                    plan.RemoveUsings.Add(u.Start);
                    removedNow.Add((plan, u.Start, name));
                }
            }
        }
        bool createdChanged = PruneCreatedTypeUsings();
        if (removedNow.Count == 0 && !createdChanged)
        {
            return;
        }
        _log.WriteLine($"  removing {removedNow.Count} usings that became unused; recompiling...");
        RenderAll();
        CompileFinal();
        var errors = NewErrors(_final);
        if (errors.Count > 0)
        {
            foreach (var (plan, start, name) in removedNow)
            {
                if (start >= 0)
                {
                    plan.RemoveUsings.Remove(start);
                }
                else
                {
                    plan.AddUsings.Add(name);
                }
            }
            _report.Warn("using-cleanup-reverted", $"removing unused usings caused {errors.Count} errors; kept them");
            RenderAll();
            CompileFinal();
        }
        RecordCompileResults(NewErrors(_final));
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Report and writes
    // ------------------------------------------------------------------------------------------------------------------

    private void ReportEdits()
    {
        foreach (DocPlan plan in _plans.Values.OrderBy(p => p.Doc.Key, StringComparer.OrdinalIgnoreCase))
        {
            CsDoc d = plan.Doc;
            if (plan.AppliedEdits == null)
            {
                continue;
            }
            LineIndex lines = LinesOf(d);
            string rel = RelOf(d);
            string outRel = _repo.Rel(d.OutFull);
            foreach (TextEdit e in plan.AppliedEdits)
            {
                var (line, col) = lines.Get(e.Start);
                string file = rel;
                int reportedLine = line;
                if (d.ExtractedFrom != null && d.LineMap.FirstOrDefault(m => line >= m.NewLine && line < m.NewLine + m.Count) is { Count: > 0 } map)
                {
                    file = d.ExtractedFrom;
                    reportedLine = map.OrigLine + (line - map.NewLine);
                }
                _report.Edits.Add(new EditRecord
                {
                    File = file,
                    NewFile = outRel != file || d.Created ? outRel : null,
                    Line = reportedLine,
                    Column = col,
                    Kind = e.Kind,
                    Before = d.Text0.Substring(e.Start, e.Length),
                    After = e.NewText,
                    BeforeLine = lines.LineText(line).Trim(),
                });
            }
        }
    }

    private static bool IsEmptyFile(string text, CsDoc d)
    {
        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(text, d.Projects[0].ParseOptions).GetCompilationUnitRoot();
        return root.AttributeLists.Count == 0 && !root.DescendantNodes().OfType<MemberDeclarationSyntax>().Any(m => m is not BaseNamespaceDeclarationSyntax);
    }

    private void CollectWrites()
    {
        foreach (CsDoc d in Docs.Where(d => d.Editable))
        {
            string text = d.Text1 ?? d.Text0;
            if (d.Created)
            {
                Writes.Add((d.OutFull, text, d.Bom, null));
            }
            else if (!string.Equals(d.OutFull, d.OrigFull, StringComparison.Ordinal))
            {
                // Includes case-only renames: Apply deletes the old spelling first, then writes the new one.
                Writes.Add((d.OutFull, text, d.Bom, d.OrigFull));
                _report.FileOps.Add(new FileOp { Op = "move", From = _repo.Rel(d.OrigFull!), To = _repo.Rel(d.OutFull) });
            }
            else if (d.Emptied || (_dissolveSpans.ContainsKey(d) && IsEmptyFile(text, d)))
            {
                Deletes.Add(d.OrigFull!);
                if (!d.Emptied)
                {
                    _report.FileOps.Add(new FileOp { Op = "delete", From = _repo.Rel(d.OrigFull!), Note = "dissolved type was the file's only content" });
                }
            }
            else if (text != _origText[d.Key])
            {
                Writes.Add((d.OutFull, text, d.Bom, null));
            }
        }
        foreach (var kv in FileMoves.Where(kv => !kv.Key.StartsWith("(new)", StringComparison.Ordinal)))
        {
            _report.Maps.Files.Add(new NameMapEntry { Old = kv.Key, New = kv.Value });
        }
    }
}
