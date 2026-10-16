using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RenameApply.CSharp;

// Phase 0: file layout. Decides where every file goes and extracts top-level types whose spec newFile differs from the
// destination of the file that declares them. Extraction is purely textual and keeps namespace and usings, so the
// compilation is unchanged (verified); the rename phase then treats the new file like any other.
public sealed partial class CSharpPass
{
    private sealed record Extraction(CsDoc Doc, string FromRel, CsDoc Source, string TypeKey, int FromLine, int ToLine);

    private readonly List<Extraction> _extractions = new();

    // Source file (original full path) -> files created from it by extraction (final full paths); project links follow.
    // Also created types placed next to a linked file: projects that link the sibling link the new file.
    public Dictionary<string, List<string>> ExtractedFiles
    {
        get
        {
            var map = _extractions.Where(x => x.Source.OrigFull != null).GroupBy(x => x.Source.OrigFull!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Doc.OutFull).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
            foreach (var (sibling, created) in _createdLinks)
            {
                if (!map.TryGetValue(sibling, out var list))
                {
                    map[sibling] = list = new List<string>();
                }
                list.AddRange(created);
            }
            return map;
        }
    }
    private readonly Dictionary<CsDoc, string> _fileTarget = new();   // original doc -> target rel path

    private static string SyntacticKey(MemberDeclarationSyntax decl)
    {
        var nsParts = decl.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString());
        string ns = string.Join(".", nsParts);
        (string name, int arity) = decl switch
        {
            TypeDeclarationSyntax t => (t.Identifier.ValueText, t.TypeParameterList?.Parameters.Count ?? 0),
            EnumDeclarationSyntax e => (e.Identifier.ValueText, 0),
            DelegateDeclarationSyntax d => (d.Identifier.ValueText, d.TypeParameterList?.Parameters.Count ?? 0),
            _ => ("", 0),
        };
        return (ns.Length > 0 ? ns + "." : "") + name + (arity > 0 ? "`" + arity : "");
    }

    private static IEnumerable<MemberDeclarationSyntax> TopLevelTypes(CompilationUnitSyntax root) =>
        root.DescendantNodes(n => n is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
            .OfType<MemberDeclarationSyntax>()
            .Where(m => m is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);

    private static string IdentifierOf(MemberDeclarationSyntax m) => m switch
    {
        BaseTypeDeclarationSyntax t => t.Identifier.ValueText,
        DelegateDeclarationSyntax d => d.Identifier.ValueText,
        _ => "",
    };

    // Same file on a case-insensitive file system (identity of a file).
    private bool SamePath(string relA, string relB) => string.Equals(RepoPaths.Norm(relA), RepoPaths.Norm(relB), StringComparison.OrdinalIgnoreCase);

    // Same spelling (a case-only difference is a rename the spec asked for).
    internal static bool SameSpelling(string a, string b) => string.Equals(RepoPaths.Norm(a), RepoPaths.Norm(b), StringComparison.Ordinal);

    private void Phase0Split()
    {
        var originals = Docs.Where(d => d.Editable && !d.Created).ToList();
        var roots = new Dictionary<CsDoc, CompilationUnitSyntax>();
        var declaredIn = new Dictionary<string, List<CsDoc>>(StringComparer.Ordinal);
        foreach (CsDoc d in originals)
        {
            CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(d.Text0, d.Projects[0].ParseOptions).GetCompilationUnitRoot();
            roots[d] = root;
            foreach (MemberDeclarationSyntax m in TopLevelTypes(root))
            {
                string key = SyntacticKey(m);
                if (!declaredIn.TryGetValue(key, out List<CsDoc>? list))
                {
                    declaredIn[key] = list = new List<CsDoc>();
                }
                if (!list.Contains(d))
                {
                    list.Add(d);
                }
            }
        }
        var explicitMoves = _m.SourceFileMoves.ToDictionary(x => x.Old, x => x.New, StringComparer.OrdinalIgnoreCase);
        foreach (string old in explicitMoves.Keys)
        {
            if (!originals.Any(d => SamePath(_repo.Rel(d.OrigFull!), old)))
            {
                _report.Error("spec-filemove-missing", $"fileMoves: {old} is not a C# file of any project under Source/");
            }
        }
        // Is doc the spec's oldFile of the type (explicit, or the only declaring file)?
        bool IsOldFileOf(TypeRename tr, CsDoc d, string key)
        {
            if (tr.Entry.OldFile != null)
            {
                return SamePath(tr.Entry.OldFile, _repo.Rel(d.OrigFull!));
            }
            var files = declaredIn.GetValueOrDefault(key) ?? new List<CsDoc>();
            if (files.Count > 1 && tr.Entry.NewFile != null)
            {
                _report.Error("spec-oldfile-required", $"type {tr.Old.Metadata} is partial over {files.Count} files; its newFile needs an explicit oldFile");
            }
            return files.Count == 1 && files[0] == d;
        }

        foreach (CsDoc d in originals)
        {
            string rel = _repo.Rel(d.OrigFull!);
            string target = rel;
            string stem = Path.GetFileName(rel).Split('.')[0];
            CompilationUnitSyntax root = roots[d];
            var tops = TopLevelTypes(root).ToList();
            // The type the file is named after; a single-type file whose name differs (Patch class in Harmony_X.cs) is its own primary.
            MemberDeclarationSyntax? primary = tops.FirstOrDefault(m => IdentifierOf(m) == stem) ?? (tops.Count == 1 ? tops[0] : null);
            string? primaryTarget = null;
            if (primary != null && _m.TypesByOldMetadata.TryGetValue(SyntacticKey(primary), out TypeRename? ptr) && ptr.Entry.NewFile != null
                && IsOldFileOf(ptr, d, SyntacticKey(primary)))
            {
                primaryTarget = RepoPaths.Norm(ptr.Entry.NewFile);
            }
            if (explicitMoves.TryGetValue(rel, out string? moved))
            {
                if (primaryTarget != null && !SamePath(primaryTarget, moved))
                {
                    _report.Error("spec-filemove-conflict", $"fileMoves sends {rel} to {moved} but type {SyntacticKey(primary!)} has newFile {primaryTarget}");
                }
                target = moved;
            }
            else if (primaryTarget != null)
            {
                target = primaryTarget;
            }
            _fileTarget[d] = target;

            // Types that must leave this file.
            var leaving = new List<(MemberDeclarationSyntax Decl, string NewRel, string Key)>();
            foreach (MemberDeclarationSyntax m in tops)
            {
                string key = SyntacticKey(m);
                if (!_m.TypesByOldMetadata.TryGetValue(key, out TypeRename? tr) || tr.Entry.NewFile == null || !IsOldFileOf(tr, d, key))
                {
                    continue;
                }
                string newRel = RepoPaths.Norm(tr.Entry.NewFile);
                if (!SamePath(newRel, target))
                {
                    if (m.Parent is not BaseNamespaceDeclarationSyntax)
                    {
                        _report.Error("extract-unsupported", $"type {key} is in the global namespace; cannot extract", rel);
                        continue;
                    }
                    leaving.Add((m, newRel, key));
                }
                else if (!SameSpelling(newRel, target))
                {
                    // A type that stays asks for a case-only rename of its file (no primary type matched the old stem).
                    target = newRel;
                    _fileTarget[d] = target;
                }
            }
            if (leaving.Count == 0)
            {
                continue;
            }
            // A、B、C 不能拆成保留 A/C 的一个文件加 B 文件；编译输入无法表达这种交错。
            // 明确拒绝该规格，要求按连续声明分组或继续拆分，不能在验证时悄悄换序。
            var destinations = tops.Select(t => leaving.FirstOrDefault(l => l.Decl == t).NewRel ?? target).ToList();
            var seenDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? previousDestination = null;
            foreach (string destination in destinations)
            {
                if (!string.Equals(destination, previousDestination, StringComparison.OrdinalIgnoreCase)
                    && !seenDestinations.Add(destination))
                {
                    _report.Error("compile-order-interleave", $"{rel}: declarations assigned to {destination} are interrupted by another output file; split into contiguous declaration groups", rel);
                }
                previousDestination = destination;
            }
            d.OrderPosition = tops.FirstOrDefault(t => !leaving.Any(l => l.Decl == t))?.SpanStart ?? 0;
            foreach (var group in leaving.GroupBy(l => l.NewRel, StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First();
                var nsDecl = (BaseNamespaceDeclarationSyntax)first.Decl.Parent!;
                if (group.Any(g => g.Decl.Parent != nsDecl))
                {
                    _report.Error("extract-unsupported", $"types extracted into {group.Key} come from different namespace blocks", rel);
                    continue;
                }
                string nl = d.NewLine;
                var sb = new StringBuilder();
                foreach (ExternAliasDirectiveSyntax ea in root.Externs)
                {
                    sb.Append(ea.ToString().Trim()).Append(nl);
                }
                foreach (UsingDirectiveSyntax u in root.Usings.Where(u => u.GlobalKeyword.IsKind(SyntaxKind.None)))
                {
                    sb.Append(u.ToString().Trim()).Append(nl);
                }
                if (root.Externs.Count > 0 || root.Usings.Any(u => u.GlobalKeyword.IsKind(SyntaxKind.None)))
                {
                    sb.Append(nl);
                }
                string body = string.Join(nl, group.Select(g => DeclText(g.Decl, nl)));
                var origLines = new LineIndex(d.Text0);
                var lineMap = new List<(int, int, int)>();
                if (nsDecl is FileScopedNamespaceDeclarationSyntax)
                {
                    sb.Append("namespace ").Append(nsDecl.Name.ToString()).Append(';').Append(nl).Append(nl);
                    foreach (UsingDirectiveSyntax u in nsDecl.Usings)
                    {
                        sb.Append(u.ToString().Trim()).Append(nl);
                    }
                    if (nsDecl.Usings.Count > 0)
                    {
                        sb.Append(nl);
                    }
                    sb.Append(body);
                }
                else
                {
                    sb.Append("namespace ").Append(nsDecl.Name.ToString()).Append(nl).Append('{').Append(nl);
                    foreach (UsingDirectiveSyntax u in nsDecl.Usings)
                    {
                        sb.Append("    ").Append(u.ToString().Trim()).Append(nl);
                    }
                    if (nsDecl.Usings.Count > 0)
                    {
                        sb.Append(nl);
                    }
                    sb.Append(body).Append('}').Append(nl);
                }
                string newFull = _repo.Full(group.Key);
                if (_docByKey.ContainsKey("new:" + newFull))
                {
                    _report.Error("extract-collision", $"two files extract types into {group.Key}", rel);
                    continue;
                }
                var created = new CsDoc
                {
                    Key = "new:" + newFull,
                    OrigFull = null,
                    OutFull = newFull,
                    Text0 = sb.ToString(),
                    Bom = d.Bom,
                    NewLine = nl,
                    Created = true,
                    Editable = true,
                    ExtractedFrom = rel,
                    OrderSource = d.OrigFull,
                    OrderPosition = first.Decl.SpanStart,
                };
                created.Projects.AddRange(d.Projects);
                // Line map: every extracted declaration's lines, for edit records that point at the source file.
                {
                    string pre = sb.ToString().Substring(0, sb.ToString().Length - body.Length - (nsDecl is FileScopedNamespaceDeclarationSyntax ? 0 : ("}" + nl).Length));
                    int newLine = pre.Count(c => c == '\n') + 1;
                    foreach (var g in group)
                    {
                        string dt = DeclText(g.Decl, nl);
                        int declLines = dt.Count(c => c == '\n');
                        string full = g.Decl.ToFullString();
                        int skipped = full.Split('\n').TakeWhile(l => l.Trim().Length == 0).Count();
                        int origStart = origLines.Get(g.Decl.FullSpan.Start).Line + skipped;
                        created.LineMap.Add((newLine, origStart, declLines));
                        newLine += declLines + 1;
                    }
                }
                _docByKey[created.Key] = created;
                Docs.Add(created);
                var lines = new LineIndex(d.Text0);
                foreach (var g in group)
                {
                    _extractions.Add(new Extraction(created, rel, d, g.Key, lines.Get(g.Decl.SpanStart).Line, lines.Get(g.Decl.Span.End).Line));
                }
            }
            // Remove the extracted declarations (with their leading trivia: comments and the separating blank line).
            var removals = leaving.Select(l => new TextEdit(l.Decl.FullSpan.Start, l.Decl.FullSpan.Length, "", "extract-type")).ToList();
            d.Text0 = TextEdits.Apply(d.Text0, TextEdits.Normalize(removals, rel));
            d.Extracted = true;
            if (leaving.Count == tops.Count)
            {
                CompilationUnitSyntax rest = CSharpSyntaxTree.ParseText(d.Text0, d.Projects[0].ParseOptions).GetCompilationUnitRoot();
                if (rest.AttributeLists.Count == 0 && !TopLevelTypes(rest).Any())
                {
                    d.Emptied = true;
                    _report.FileOps.Add(new FileOp { Op = "delete", From = rel, Note = "every type was extracted into its own file" });
                }
            }
        }
        foreach (Extraction x in _extractions)
        {
            _report.FileOps.Add(new FileOp { Op = "create", From = x.FromRel, To = _repo.Rel(x.Doc.OutFull), Note = $"extracted {x.TypeKey} (lines {x.FromLine}-{x.ToLine})" });
            _report.Edits.Add(new EditRecord
            {
                File = x.FromRel, Line = x.FromLine, Column = 1, Kind = "extract-type", Before = x.TypeKey, After = _repo.Rel(x.Doc.OutFull),
                Note = "later line numbers of this file refer to the text after extraction",
            });
        }
    }

    private static string DeclText(MemberDeclarationSyntax decl, string nl)
    {
        string full = decl.ToFullString();
        // Drop leading blank lines; keep comments and attributes.
        string[] lines = full.Split('\n');
        int i = 0;
        while (i < lines.Length - 1 && lines[i].Trim().Length == 0)
        {
            i++;
        }
        string text = string.Join("\n", lines.Skip(i));
        if (!text.EndsWith('\n'))
        {
            text += nl;
        }
        return text;
    }

    private void CheckPhase0AgainstOriginal()
    {
        // The extraction must not change what compiles: compare against the untouched sources.
        var saved = Docs.Where(d => d.Extracted).ToDictionary(d => d, d => d.Text0);
        var created = Docs.Where(d => d.Created).ToList();
        foreach (CsDoc d in saved.Keys)
        {
            d.Text0 = _origText[d.Key];
        }
        foreach (CsDoc c in created)
        {
            Docs.Remove(c);
        }
        var original = BuildAll(final: false, out _, out _);
        _originalMain = original[Main];
        foreach (CsDoc d in saved.Keys)
        {
            d.Text0 = saved[d];
        }
        Docs.AddRange(created);
        foreach (ProjectModel p in Projects)
        {
            var before = original[p].GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).Select(x => ErrorSignature(p, x)).OrderBy(s => s).ToList();
            var after = _baselineErrors[p].OrderBy(s => s).ToList();
            if (!before.SequenceEqual(after))
            {
                _report.Error("extract-changed-compilation", $"{p.Name}: extracting types changed the compile result ({before.Count} -> {after.Count} errors): {string.Join(" / ", after.Except(before).Take(5))}");
            }
        }
    }

    private void ComputeFileTargets()
    {
        foreach (var (d, rel) in _fileTarget)
        {
            if (!SameSpelling(rel, _repo.Rel(d.OrigFull!)))
            {
                d.OutFull = _repo.Full(rel);
                FileMoves[_repo.Rel(d.OrigFull!)] = rel;
                // The file must stay inside the project directory that compiles it by default globbing.
                ProjectModel? home = d.Projects.FirstOrDefault(p => d.OrigFull!.StartsWith(p.Directory, StringComparison.OrdinalIgnoreCase));
                if (home != null && !d.OutFull.StartsWith(home.Directory, StringComparison.OrdinalIgnoreCase))
                {
                    _report.Error("move-cross-project", $"{_repo.Rel(d.OrigFull!)} -> {rel} leaves project {home.Name}");
                }
                if (Path.GetExtension(rel) != ".cs")
                {
                    _report.Error("move-invalid", $"{rel}: C# files must keep the .cs extension");
                }
            }
        }
        foreach (Extraction x in _extractions.GroupBy(x => x.Doc).Select(g => g.First()))
        {
            FileMoves.TryAdd("(new) " + _repo.Rel(x.Doc.OutFull), _repo.Rel(x.Doc.OutFull));
        }
        // Collisions: two outputs on one path, or an output on an existing file that stays.
        var outs = Docs.Where(d => d.Editable).GroupBy(d => d.OutFull, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1);
        foreach (var g in outs)
        {
            _report.Error("move-collision", $"several files end up at {_repo.Rel(g.Key)}: {string.Join(", ", g.Select(d => d.OrigFull != null ? _repo.Rel(d.OrigFull) : "(extracted)"))}");
        }
        foreach (CsDoc d in Docs.Where(d => d.Editable && (d.Created || !string.Equals(d.OutFull, d.OrigFull, StringComparison.OrdinalIgnoreCase))))
        {
            if (File.Exists(d.OutFull) && !Docs.Any(o => string.Equals(o.OrigFull, d.OutFull, StringComparison.OrdinalIgnoreCase) && !string.Equals(o.OutFull, o.OrigFull, StringComparison.OrdinalIgnoreCase)))
            {
                _report.Error("move-target-exists", $"{_repo.Rel(d.OutFull)} already exists and is not moved away");
            }
        }
    }
}
