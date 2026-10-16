using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RenameApply.CSharp;

// R2 member operations: createTypes, memberMoves (with per-reference copies), memberDeletes, dissolveTypes,
// attributeArgumentRewrites and C# textEdits. Moved declarations keep their text (attributes such as [MayRequire],
// comments, initializers); references are re-pointed with the same site/requirement/verification machinery as renames.
public sealed partial class CSharpPass
{
    private sealed class MovedMember
    {
        public required ISymbol Symbol { get; init; }
        public required string OldId { get; init; }
        public required string Kind { get; init; }                 // move | delete
        public required string DefaultTarget { get; init; }        // metadata name ("" for delete)
        public List<ReferenceRule> Rules { get; } = new();
        public HashSet<string> Targets { get; } = new(StringComparer.Ordinal);
        public string? Accessibility { get; init; }
        public required CsDoc Doc { get; init; }
        public required MemberDeclarationSyntax Decl { get; init; }
        public int SpanStart { get; set; }
        public int SpanEnd { get; set; }
        public string Module { get; init; } = "";
        public int? SortKey { get; init; }                         // spec order (MemberMoveEntry.Order == "spec")
        public string? MovedText { get; set; }                     // final text (inner edits applied), set while rendering
        public List<TextEdit> Inner { get; set; } = new();          // inner edits relative to SpanStart
    }

    private sealed class TargetType
    {
        public required string FullName { get; init; }
        public required string Namespace { get; init; }
        public required string Name { get; init; }
        public required bool Created { get; init; }
        public CsDoc? Doc { get; set; }                           // existing declaring doc or the created doc
        public int InsertPos { get; set; } = -1;                   // Text0 position (existing types)
        public string Kind { get; init; } = "staticClass";
        public string Accessibility { get; init; } = "internal";
        public string? Header { get; init; }
        public List<string> EnumMembers { get; init; } = new();
        public string Indent { get; set; } = "    ";
        public List<MovedMember> Incoming { get; } = new();
    }

    private readonly Dictionary<string, MovedMember> _moved = new(StringComparer.Ordinal);    // old member doc id -> op
    private readonly Dictionary<string, List<string>> _createdLinks = new(StringComparer.OrdinalIgnoreCase);   // linked sibling -> created files
    private readonly Dictionary<string, TargetType> _targetTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<CsDoc, List<(int Start, int End, string Type)>> _dissolveSpans = new();
    private readonly Dictionary<CsDoc, List<AttributeArgumentEntry>> _attrRewritesByDoc = new();
    private bool HasMemberOps => _moved.Count > 0 || _targetTypes.Count > 0 || _m.Spec.AttributeArgumentRewrites.Count > 0 || _m.Spec.TextEdits.Count > 0;

    private static string Ns(string full) => full.Contains('.') ? full.Substring(0, full.LastIndexOf('.')) : "";
    private static string Short(string full) => full.Substring(full.LastIndexOf('.') + 1);

    // ------------------------------------------------------------------------------------------------------------------
    // Spec resolution (after the baseline compile)
    // ------------------------------------------------------------------------------------------------------------------

    private void BuildMemberOps()
    {
        foreach (CreateTypeEntry e in _m.Spec.CreateTypes)
        {
            string full = e.FullName.Trim();
            string fileFull = _repo.Full(RepoPaths.Norm(e.File));
            if (Comp0[Main].GetTypeByMetadataName(full) != null)
            {
                _report.Error("create-type-exists", $"createTypes: {full} already exists");
                continue;
            }
            if (File.Exists(fileFull))
            {
                _report.Error("create-type-file-exists", $"createTypes: {e.File} already exists");
                continue;
            }
            var tt = new TargetType
            {
                FullName = full, Namespace = Ns(full), Name = Short(full), Created = true, Kind = e.Kind, Accessibility = e.Accessibility ?? "internal",
                Header = e.Header, EnumMembers = e.Members ?? new(),
            };
            var doc = new CsDoc
            {
                Key = "createtype:" + fileFull, OrigFull = null, OutFull = fileFull, Text0 = "", Bom = Docs.First(d => d.Projects.Contains(Main)).Bom,
                NewLine = Docs.First(d => d.Projects.Contains(Main) && !d.Created).NewLine, Created = true, Editable = true, FinalOnly = true,
            };
            doc.Projects.Add(Main);
            // linkWith: projects that link one of these files (by path) compile and link the new file too.
            foreach (ProjectModel p in Projects.Where(p => p != Main))
            {
                CsDoc? sibling = Docs.FirstOrDefault(x => x.Projects.Contains(p) && x.OrigFull != null && !x.OrigFull.StartsWith(p.Directory, StringComparison.OrdinalIgnoreCase)
                                                          && (e.LinkWith ?? new()).Any(l => string.Equals(_repo.Full(RepoPaths.Norm(l)), x.OrigFull, StringComparison.OrdinalIgnoreCase)));
                if (sibling != null)
                {
                    doc.Projects.Add(p);
                    if (!_createdLinks.TryGetValue(sibling.OrigFull!, out var list))
                    {
                        _createdLinks[sibling.OrigFull!] = list = new List<string>();
                    }
                    list.Add(fileFull);
                    _report.Info("created-type-linked", $"{_repo.Rel(fileFull)} is linked into {p.Name} next to {_repo.Rel(sibling.OutFull)}");
                }
            }
            Docs.Add(doc);
            _docByKey[doc.Key] = doc;
            tt.Doc = doc;
            doc.Text1 = RenderCreatedType(tt);
            _targetTypes[full] = tt;
            _report.FileOps.Add(new FileOp { Op = "create", From = "(spec)", To = _repo.Rel(fileFull), Note = $"createTypes {full} ({e.Kind})" });
            _report.Maps.CreatedFiles.Add(new NameMapEntry { Old = "", New = _repo.Rel(fileFull), Module = e.Module });
        }

        for (int ei = 0; ei < _m.Spec.MemberMoves.Count; ei++)
        {
            MemberMoveEntry mv = _m.Spec.MemberMoves[ei];
            if (mv.Order is not (null or "source" or "spec"))
            {
                _report.Error("spec-invalid", $"memberMoves to {mv.To}: order '{mv.Order}' (source | spec)");
            }
            for (int mi = 0; mi < mv.Members.Count; mi++)
            {
                AddMovedMember(mv.Members[mi], "move", mv.To, mv.ReferenceRules, mv.Accessibility, mv.Module, mv.Order == "spec" ? ei * 10000 + mi : null);
            }
        }
        foreach (MemberDeleteEntry del in _m.Spec.MemberDeletes)
        {
            foreach (string spec in del.Members)
            {
                AddMovedMember(spec, "delete", "", null, null, del.Module, null);
            }
        }
        foreach (DissolveTypeEntry dt in _m.Spec.DissolveTypes)
        {
            INamedTypeSymbol? t = Comp0[Main].GetTypeByMetadataName(dt.Type);
            if (t == null)
            {
                _report.Error("dissolve-type-missing", $"dissolveTypes: {dt.Type} not found");
                continue;
            }
            var leftover = t.GetMembers().Where(m => !m.IsImplicitlyDeclared && m is not INamedTypeSymbol && !(m is IMethodSymbol { MethodKind: MethodKind.StaticConstructor })
                                                     && !_moved.ContainsKey(m.OriginalDefinition.GetDocumentationCommentId() ?? "")).ToList();
            if (leftover.Count > 0)
            {
                _report.Error("dissolve-type-not-empty", $"dissolveTypes: {dt.Type} keeps {leftover.Count} member(s) after the moves: {string.Join(", ", leftover.Take(8).Select(m => m.Name))}");
                continue;
            }
            foreach (SyntaxReference r in t.DeclaringSyntaxReferences)
            {
                var decl = (TypeDeclarationSyntax)r.GetSyntax();
                if (!_treeDoc0.TryGetValue(r.SyntaxTree, out CsDoc? d))
                {
                    continue;
                }
                if (!_dissolveSpans.TryGetValue(d, out var list))
                {
                    _dissolveSpans[d] = list = new();
                }
                list.Add((decl.FullSpan.Start, decl.FullSpan.End, dt.Type));
            }
            _report.Maps.Members.Add(new MemberMapEntry { Old = dt.Type, New = "", Kind = "dissolve", Module = dt.Module });
        }
        foreach (AttributeArgumentEntry a in _m.Spec.AttributeArgumentRewrites)
        {
            if (Comp0[Main].GetTypeByMetadataName(a.Attribute) == null && !_targetTypes.ContainsKey(a.Attribute))
            {
                _report.Error("attribute-rewrite-missing", $"attributeArgumentRewrites: attribute {a.Attribute} not found");
            }
        }
    }

    private void AddMovedMember(string spec, string kind, string to, List<ReferenceRule>? rules, string? accessibility, string module, int? sortKey)
    {
        ISymbol? sym = ResolveMember(spec, kind == "delete" ? "memberDeletes" : "memberMoves");
        if (sym == null)
        {
            return;
        }
        if (!sym.IsStatic)
        {
            _report.Error("member-move-instance", $"{spec}: only static members can move between types");
            return;
        }
        if (HarmonyConventionNames.Contains(sym.Name) && sym.ContainingType.GetAttributes().Any(a => a.AttributeClass?.Name == "HarmonyPatch"))
        {
            _report.Error("member-move-harmony", $"{spec}: Harmony binds this method by name inside its patch class");
            return;
        }
        if (sym.GetAttributes().Any(a => a.AttributeClass?.Name is "TweakValueAttribute" or "DebugActionAttribute"))
        {
            _report.Error("member-move-devtool", $"{spec}: [TweakValue]/[DebugAction] members are found by declaring type");
            return;
        }
        SyntaxReference? sr = sym.DeclaringSyntaxReferences.FirstOrDefault();
        if (sr == null || !_treeDoc0.TryGetValue(sr.SyntaxTree, out CsDoc? doc))
        {
            _report.Error("member-move-source", $"{spec}: declaration not found in source");
            return;
        }
        SyntaxNode node = sr.GetSyntax();
        MemberDeclarationSyntax? decl = node switch
        {
            VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax fd } => fd,
            MemberDeclarationSyntax md => md,
            _ => null,
        };
        if (decl == null || decl is FieldDeclarationSyntax { Declaration.Variables.Count: > 1 })
        {
            _report.Error("member-move-declaration", $"{spec}: declaration shape not supported (several fields in one declaration?)");
            return;
        }
        string id = sym.OriginalDefinition.GetDocumentationCommentId()!;
        if (_moved.ContainsKey(id))
        {
            _report.Error("member-move-duplicate", $"{spec}: listed twice");
            return;
        }
        var mm = new MovedMember
        {
            Symbol = sym, OldId = id, Kind = kind, DefaultTarget = to.Split('@')[0], Accessibility = accessibility, Doc = doc, Decl = decl,
            SpanStart = decl.FullSpan.Start, SpanEnd = decl.FullSpan.End, Module = module, SortKey = sortKey,
        };
        if (rules != null)
        {
            mm.Rules.AddRange(rules);
        }
        if (kind == "move")
        {
            mm.Targets.Add(mm.DefaultTarget);
            foreach (ReferenceRule r in mm.Rules)
            {
                mm.Targets.Add(r.To);
            }
            foreach (string t in mm.Targets)
            {
                TargetType? tt = ResolveTarget(t, to.Contains('@') && t == mm.DefaultTarget ? to.Split('@')[1] : null);
                if (tt == null)
                {
                    return;
                }
                if (tt.Created == false && tt.FullName == sym.ContainingType.ToDisplayString())
                {
                    _report.Error("member-move-same-type", $"{spec}: target is its own type");
                    return;
                }
                tt.Incoming.Add(mm);
                string newId = MemberIdIn(sym, t);
                _report.Maps.Members.Add(new MemberMapEntry { Old = id.Substring(2), New = newId.Substring(2), Kind = mm.Targets.Count > 1 ? "copy" : "move", Module = module });
            }
            if (mm.Targets.Count == 1)
            {
                // A single target: every binding to the old member must bind to the moved one.
                string mapped = _m.MapDocString(id);
                _defOfMappedKeys[mapped] = MemberIdIn(sym, mm.DefaultTarget);
            }
        }
        else
        {
            _report.Maps.Members.Add(new MemberMapEntry { Old = id.Substring(2), New = "", Kind = "delete", Module = module });
        }
        _moved[id] = mm;
    }

    // Doc id of a member once it lives in another type: "F:Ns.Target.Name" (methods keep their parameter list).
    private string MemberIdIn(ISymbol sym, string target)
    {
        string old = sym.OriginalDefinition.GetDocumentationCommentId()!;
        string prefix = old.Substring(0, 2);
        string rest = old.Substring(2);
        string containing = sym.ContainingType.OriginalDefinition.GetDocumentationCommentId()!.Substring(2);
        string tail = rest.Substring(containing.Length);   // ".Name(...)"
        return prefix + target.Replace('+', '.') + tail;
    }

    private TargetType? ResolveTarget(string full, string? fileHint)
    {
        if (_targetTypes.TryGetValue(full, out TargetType? tt))
        {
            return tt;
        }
        INamedTypeSymbol? t = Comp0[Main].GetTypeByMetadataName(full);
        if (t == null)
        {
            _report.Error("member-move-target-missing", $"target type {full} neither exists nor is in createTypes");
            return null;
        }
        var decls = t.DeclaringSyntaxReferences.Select(r => (TypeDeclarationSyntax)r.GetSyntax()).ToList();
        TypeDeclarationSyntax decl = decls.FirstOrDefault(d => fileHint != null && string.Equals(_repo.Rel(d.SyntaxTree.FilePath), RepoPaths.Norm(fileHint), StringComparison.OrdinalIgnoreCase))
                                     ?? decls.FirstOrDefault(d => Path.GetFileNameWithoutExtension(d.SyntaxTree.FilePath) == t.Name)
                                     ?? decls[0];
        if (!_treeDoc0.TryGetValue(decl.SyntaxTree, out CsDoc? doc))
        {
            _report.Error("member-move-target-source", $"target type {full}: declaration not in an editable file");
            return null;
        }
        // Before a static constructor (DefOf classes end with it), else before the closing brace.
        MemberDeclarationSyntax? cctor = decl.Members.OfType<ConstructorDeclarationSyntax>().FirstOrDefault(c => c.Modifiers.Any(SyntaxKind.StaticKeyword));
        int pos = cctor?.FullSpan.Start ?? decl.CloseBraceToken.FullSpan.Start;
        string indent = decl.Members.Count > 0 ? IndentOf(doc.Text0, decl.Members[0].SpanStart) : IndentOf(doc.Text0, decl.SpanStart) + "    ";
        tt = new TargetType
        {
            FullName = full, Namespace = Ns(full), Name = Short(full), Created = false, Doc = doc, InsertPos = pos, Indent = indent,
        };
        _targetTypes[full] = tt;
        return tt;
    }

    private static IEnumerable<INamedTypeSymbol> EnclosingTypes(ISymbol s)
    {
        for (INamedTypeSymbol? t = s as INamedTypeSymbol ?? s.ContainingType; t != null; t = t.ContainingType)
        {
            yield return t;
        }
    }

    private static string IndentOf(string text, int pos)
    {
        int ls = text.LastIndexOf('\n', Math.Max(0, pos - 1)) + 1;
        int e = ls;
        while (e < text.Length && (text[e] == ' ' || text[e] == '\t'))
        {
            e++;
        }
        return text.Substring(ls, e - ls);
    }

    private string TargetFor(MovedMember mm, CsDoc referencing)
    {
        string rel = _repo.Rel(referencing.OutFull);
        foreach (ReferenceRule r in mm.Rules)
        {
            if (rel.StartsWith(RepoPaths.Norm(r.PathPrefix), StringComparison.OrdinalIgnoreCase))
            {
                return r.To;
            }
        }
        return mm.DefaultTarget;
    }

    private MovedMember? MovedSpanAt(CsDoc d, int pos) =>
        _moved.Values.FirstOrDefault(m => m.Doc == d && pos >= m.SpanStart && pos < m.SpanEnd);

    private bool InDissolvedSpan(CsDoc d, int pos) =>
        _dissolveSpans.TryGetValue(d, out var list) && list.Any(s => pos >= s.Start && pos < s.End);

    // ------------------------------------------------------------------------------------------------------------------
    // Walk hooks
    // ------------------------------------------------------------------------------------------------------------------

    // Returns true when the name was handled as a moved/deleted member reference.
    private bool MovedMemberSite(ProjectModel p, DocPlan plan, SemanticModel model, SimpleNameSyntax n, ISymbol? sym, Site site, bool primary)
    {
        if (_moved.Count == 0 || sym == null || sym.ContainingType == null || !IsOwned(sym.ContainingType))
        {
            return false;
        }
        CsDoc d = plan.Doc;
        string? key = MemberKey(sym);
        MovedMember? here = MovedSpanAt(d, n.SpanStart);
        // Inside a moved declaration: unqualified uses of members that stay in (or move away from) the old type get qualified.
        if (here != null && LeftOf(n) == null && IsLeftmost(n) && sym is IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol && sym.IsStatic
            && SymbolEqualityComparer.Default.Equals(sym.ContainingType?.OriginalDefinition, here.Symbol.ContainingType.OriginalDefinition)
            && !(sym is IMethodSymbol { MethodKind: MethodKind.LocalFunction }))
        {
            if (key != null && _moved.TryGetValue(key, out MovedMember? other) && other.Kind == "move" && TargetFor(other, d) == TargetFor(here, d))
            {
                site.ExpectedKey = MemberIdIn(other.Symbol, TargetFor(other, d));
                return true;   // moves along into the same type: stays unqualified
            }
            string owner = key != null && _moved.TryGetValue(key, out MovedMember? o2) && o2.Kind == "move" ? TargetFor(o2, d) : MetadataKey(sym.ContainingType);
            QualifyMemberSite(p, plan, model, n, site, owner);
            site.ExpectedKey = key != null && _moved.TryGetValue(key, out MovedMember? o3) && o3.Kind == "move" ? MemberIdIn(sym, owner) : MapKey(KeyOf(sym), p);
            return true;
        }
        if (key == null || !_moved.TryGetValue(key, out MovedMember? mm) || n.Identifier.ValueText != sym.Name)
        {
            return false;
        }
        if (mm.Kind == "delete")
        {
            if (here != mm && !InDissolvedSpan(d, n.SpanStart) && primary)
            {
                _report.Error("member-delete-referenced", $"{mm.OldId.Substring(2)} is deleted but still referenced", RelOf(d), LinesOf(d).Get(n.SpanStart).Line, LinesOf(d).LineText(LinesOf(d).Get(n.SpanStart).Line).Trim());
            }
            return true;
        }
        string target = TargetFor(mm, d);
        site.ExpectedKey = MemberIdIn(sym, target);
        SyntaxNode? left = LeftOf(n);
        bool insideTarget = model.GetEnclosingSymbol(n.SpanStart) is ISymbol enc && EnclosingTypes(enc).Any(t => MetadataKey(t) == target);
        if (left != null)
        {
            ISymbol? ls = Bind(model, left);
            if (ls is INamedTypeSymbol lt && SymbolEqualityComparer.Default.Equals(lt.OriginalDefinition, sym.ContainingType!.OriginalDefinition))
            {
                SyntaxToken lid = left switch
                {
                    SimpleNameSyntax s => s.Identifier,
                    QualifiedNameSyntax q => q.Right.Identifier,
                    MemberAccessExpressionSyntax ma => ma.Name.Identifier,
                    AliasQualifiedNameSyntax aq => aq.Name.Identifier,
                    _ => default,
                };
                if (lid == default)
                {
                    _report.Error("member-move-reference", "unsupported qualifier form", RelOf(d), LinesOf(d).Get(n.SpanStart).Line);
                    return true;
                }
                Site owner = GetOrCreateSite(plan, lid);
                if (insideTarget && n.Parent is MemberAccessExpressionSyntax { Expression: var ex } ma0 && ex == left)
                {
                    // "Old.M" inside the target type becomes plain "M".
                    owner.Kind = SiteKind.QualifiedType;
                    owner.ReplaceStart = left.SpanStart;
                    owner.ReplaceLength = ma0.OperatorToken.Span.End - left.SpanStart;
                    owner.NewText = "";
                    owner.AnchorOffset = 0;
                    owner.EditKind = "member-owner-dropped";
                    owner.QualifiedText = null;
                    owner.ExpectedKey = null;
                    plan.Requirements.RemoveAll(r => r.Site == owner);
                    return true;
                }
                bool simple = left is SimpleNameSyntax;
                string text = simple ? Short(target) : target;
                owner.Kind = SiteKind.QualifiedType;
                owner.ReplaceStart = left.SpanStart;
                owner.ReplaceLength = left.Span.Length;
                owner.NewText = text;
                owner.AnchorOffset = text.Length - Short(target).Length;
                owner.EditKind = "member-owner";
                owner.QualifiedText = target;
                owner.QualifyNeedsGlobal = FirstSegmentShadowed(model, left.SpanStart, Ns(target), n.Parent is MemberAccessExpressionSyntax);
                owner.ExpectedKey = "T:" + target.Replace('+', '.');
                if (simple && Ns(target).Length > 0)
                {
                    plan.Requirements.RemoveAll(r => r.Site == owner);
                    plan.Requirements.Add(new Requirement { Project = p, Site = owner, Namespace = Ns(target), Kind = RequirementKind.Type });
                }
                return true;
            }
            if (primary)
            {
                _report.Warn("member-move-reference", $"reference to moved {sym.Name} through '{left}' (not its declaring type) left as is", RelOf(d), LinesOf(d).Get(n.SpanStart).Line);
            }
            return true;
        }
        if (here != null && TargetFor(here, d) == target || insideTarget)
        {
            return true;   // both move into the same type, or the reference is already inside it
        }
        if (!(n.Parent is MemberAccessExpressionSyntax ma2 && ma2.Name == n) && !(n.Parent is MemberBindingExpressionSyntax))
        {
            QualifyMemberSite(p, plan, model, n, site, target);
        }
        return true;
    }

    private void QualifyMemberSite(ProjectModel p, DocPlan plan, SemanticModel model, SimpleNameSyntax n, Site site, string owner)
    {
        string text = Short(owner) + "." + n.Identifier.Text;
        site.Kind = SiteKind.DefOfField;
        site.ReplaceStart = n.SpanStart;
        site.ReplaceLength = n.Identifier.Span.Length;
        site.NewText = text;
        site.AnchorOffset = Short(owner).Length + 1;
        site.EditKind = "member-ref-qualified";
        site.QualifiedText = owner + "." + n.Identifier.Text;
        site.QualifyNeedsGlobal = FirstSegmentShadowed(model, n.SpanStart, Ns(owner), true);
        if (Ns(owner).Length > 0)
        {
            plan.Requirements.Add(new Requirement { Project = p, Site = site, Namespace = Ns(owner), Kind = RequirementKind.Type });
        }
    }

    // [Attr("old", ...)] -> [Attr(Enum.Member, ...)] from the declaration's final path.
    private void AttributeArgumentSite(DocPlan plan, SemanticModel model, AttributeSyntax attr)
    {
        if (_m.Spec.AttributeArgumentRewrites.Count == 0)
        {
            return;
        }
        INamedTypeSymbol? cls = (model.GetSymbolInfo(attr).Symbol as IMethodSymbol)?.ContainingType;
        if (cls == null)
        {
            return;
        }
        string meta = MetadataKey(cls);
        foreach (AttributeArgumentEntry e in _m.Spec.AttributeArgumentRewrites.Where(e => e.Attribute == meta))
        {
            var args = attr.ArgumentList?.Arguments.Where(a => a.NameEquals == null && a.NameColon == null).ToList();
            if (args == null || args.Count <= e.Argument)
            {
                continue;
            }
            AttributeArgumentSyntax arg = args[e.Argument];
            string rel = _repo.Rel(plan.Doc.OutFull);
            PathValueRule? rule = e.Rules.FirstOrDefault(r => rel.StartsWith(RepoPaths.Norm(r.PathPrefix), StringComparison.OrdinalIgnoreCase));
            if (rule == null)
            {
                _report.Error("attribute-rewrite-no-rule", $"[{cls.Name}] in {rel}: no rule matches the file path", RelOf(plan.Doc), LinesOf(plan.Doc).Get(attr.SpanStart).Line);
                continue;
            }
            string value = rule.Value;
            if (value.Contains("{folder}", StringComparison.Ordinal))
            {
                string restPath = rel.Substring(RepoPaths.Norm(rule.PathPrefix).Length);
                value = value.Replace("{folder}", restPath.Split('/')[0], StringComparison.Ordinal);
            }
            string expr = string.Format(System.Globalization.CultureInfo.InvariantCulture, e.ValueFormat, value);
            plan.TriviaEdits.Add(new TextEdit(arg.Expression.SpanStart, arg.Expression.Span.Length, expr, "attribute-argument"));
            if (e.RequireNamespace != null)
            {
                plan.ExtraNamespaces.Add(e.RequireNamespace);
            }
            _report.Maps.AttributeArguments.Add(new NameMapEntry { Old = $"{rel}: {arg.Expression}", New = expr, Module = e.Module });
        }
    }

    private void TextEditSites(DocPlan plan)
    {
        string rel = RelOf(plan.Doc);
        foreach (TextEditEntry e in _m.Spec.TextEdits.Where(e => string.Equals(RepoPaths.Norm(e.File), rel, StringComparison.OrdinalIgnoreCase)))
        {
            string text = plan.Doc.Text0;
            // Spec text uses LF; the file may use CRLF.
            string oldText = TextFileData.ToNewLine(e.Old, plan.Doc.NewLine);
            string newText = TextFileData.ToNewLine(e.New, plan.Doc.NewLine);
            int i = text.IndexOf(oldText, StringComparison.Ordinal);
            if (i < 0 || text.IndexOf(oldText, i + 1, StringComparison.Ordinal) >= 0)
            {
                _report.Error("text-edit-not-unique", $"textEdits: '{e.Old}' occurs {(i < 0 ? 0 : 2)}+ times in {rel} (needs exactly one)", rel);
                continue;
            }
            plan.TriviaEdits.Add(new TextEdit(i, oldText.Length, newText, "text-edit"));
            _usedTextEdits.Add(e);
            foreach (Site s in plan.Sites.Values.Where(s => s.Start >= i && s.Start < i + oldText.Length))
            {
                s.Removed = true;   // replaced verbatim by the text edit; the compile verifies the result
            }
        }
    }

    private readonly HashSet<TextEditEntry> _usedTextEdits = new();

    public IReadOnlySet<TextEditEntry> UsedTextEdits => _usedTextEdits;

    // ------------------------------------------------------------------------------------------------------------------
    // Rendering
    // ------------------------------------------------------------------------------------------------------------------

    // Splits a plan's edits: edits inside moved/dissolved spans become the moved text; the spans are deleted.
    private List<TextEdit> ExtractMovedSpans(DocPlan plan, List<TextEdit> edits)
    {
        CsDoc d = plan.Doc;
        var spans = _moved.Values.Where(m => m.Doc == d).ToList();
        _dissolveSpans.TryGetValue(d, out var dissolved);
        if (spans.Count == 0 && dissolved == null)
        {
            return edits;
        }
        var result = new List<TextEdit>();
        foreach (MovedMember mm in spans)
        {
            var inner = edits.Where(e => e.Start >= mm.SpanStart && e.End <= mm.SpanEnd && !(e.Length == 0 && e.Start == mm.SpanEnd)).ToList();
            var innerRel = inner.Select(e => e with { Start = e.Start - mm.SpanStart }).ToList();
            if (mm.Accessibility != null && mm.Kind == "move")
            {
                SyntaxTokenList mods = mm.Decl.Modifiers;
                var acc = mods.Where(t => t.IsKind(SyntaxKind.PublicKeyword) || t.IsKind(SyntaxKind.InternalKeyword) || t.IsKind(SyntaxKind.PrivateKeyword) || t.IsKind(SyntaxKind.ProtectedKeyword)).ToList();
                if (acc.Count > 0)
                {
                    int s = acc[0].SpanStart - mm.SpanStart, e = acc[^1].Span.End - mm.SpanStart;
                    innerRel.Add(new TextEdit(s, e - s, mm.Accessibility, "member-accessibility"));
                }
                else
                {
                    innerRel.Add(new TextEdit(mm.Decl.SpanStart - mm.SpanStart + (mm.Decl.AttributeLists.Count > 0 ? mm.Decl.AttributeLists.Last().FullSpan.End - mm.Decl.SpanStart : 0), 0, mm.Accessibility + " ", "member-accessibility"));
                }
            }
            mm.Inner = TextEdits.Normalize(innerRel, RelOf(d) + " (moved member)");
            mm.MovedText = TextEdits.Apply(d.Text0.Substring(mm.SpanStart, mm.SpanEnd - mm.SpanStart), mm.Inner);
        }
        bool InSpan(TextEdit e) =>
            spans.Any(m => e.Start >= m.SpanStart && e.End <= m.SpanEnd && !(e.Length == 0 && e.Start == m.SpanEnd))
            || (dissolved?.Any(s => e.Start >= s.Start && e.End <= s.End) ?? false);
        result.AddRange(edits.Where(e => !InSpan(e)));
        foreach (MovedMember mm in spans.Where(m => !(dissolved?.Any(s => m.SpanStart >= s.Start && m.SpanEnd <= s.End) ?? false)))
        {
            result.Add(new TextEdit(mm.SpanStart, mm.SpanEnd - mm.SpanStart, "", mm.Kind == "delete" ? "member-delete" : "member-move-out"));
        }
        foreach (var s in dissolved ?? new())
        {
            result.Add(new TextEdit(s.Start, s.End - s.Start, "", "type-dissolve"));
        }
        return result;
    }

    // Moved declaration text re-indented for its new home, without leading blank lines, ending with a newline.
    private static string Reindent(string text, string fromIndent, string toIndent, string nl)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && lines[0].Trim().Length == 0)
        {
            lines.RemoveAt(0);
        }
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }
        var sb = new StringBuilder();
        foreach (string l in lines)
        {
            string line = l.Length == 0 ? l : l.StartsWith(fromIndent, StringComparison.Ordinal) ? toIndent + l.Substring(fromIndent.Length) : l;
            sb.Append(line).Append(nl);
        }
        return sb.ToString();
    }

    private string InsertionText(TargetType tt, string nl)
    {
        var docOrder = tt.Incoming.Select(m => m.Doc).Distinct().ToList();
        // Spec-ordered members first in spec order (keeps overloads together), then the rest in declaration order.
        var parts = tt.Incoming.Where(m => m.MovedText != null).OrderBy(m => m.SortKey.HasValue ? 0 : 1).ThenBy(m => m.SortKey ?? 0)
            .ThenBy(m => docOrder.IndexOf(m.Doc)).ThenBy(m => m.SpanStart)
            .Select(m => Reindent(m.MovedText!, IndentOf(m.Doc.Text0, m.Decl.SpanStart), tt.Indent, nl)).ToList();
        return string.Join(nl, parts);
    }

    private string RenderCreatedType(TargetType tt)
    {
        string nl = tt.Doc!.NewLine;
        var sb = new StringBuilder();
        // Usings of the files the members came from (unused ones are removed after the final compile).
        var usings = tt.Incoming.SelectMany(m => _plans.TryGetValue(m.Doc, out DocPlan? p) ? p.Usings.Where(u => !u.InsideNamespace && !u.NamespaceName.StartsWith('#')).Select(u => u.NamespaceName) : Enumerable.Empty<string>())
            .Concat(_createdTypeExtraUsings.GetValueOrDefault(tt.FullName) ?? new())
            .Where(u => u != tt.Namespace && !_createdTypeRemovedUsings.Contains((tt.FullName, u)))
            .Distinct(StringComparer.Ordinal).OrderBy(u => u, Comparer<string>.Create(CompareUsing)).ToList();
        foreach (string u in usings)
        {
            sb.Append("using ").Append(u).Append(';').Append(nl);
        }
        if (usings.Count > 0)
        {
            sb.Append(nl);
        }
        sb.Append("namespace ").Append(tt.Namespace).Append(';').Append(nl).Append(nl);
        foreach (string h in (tt.Header ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append("// ").Append(h.Trim()).Append(nl);
        }
        switch (tt.Kind)
        {
            case "enum":
                sb.Append(tt.Accessibility).Append(" enum ").Append(tt.Name).Append(nl).Append('{').Append(nl);
                foreach (string m in tt.EnumMembers)
                {
                    sb.Append("    ").Append(m).Append(',').Append(nl);
                }
                sb.Append('}').Append(nl);
                break;
            case "defOf":
                sb.Append("[DefOf]").Append(nl);
                sb.Append(tt.Accessibility).Append(" static class ").Append(tt.Name).Append(nl).Append('{').Append(nl);
                string members = InsertionText(tt, nl);
                if (members.Length > 0)
                {
                    sb.Append(members).Append(nl);
                }
                sb.Append("    static ").Append(tt.Name).Append("() => DefOfHelper.EnsureInitializedInCtor(typeof(").Append(tt.Name).Append("));").Append(nl);
                sb.Append('}').Append(nl);
                break;
            default:
                sb.Append(tt.Accessibility).Append(" static class ").Append(tt.Name).Append(nl).Append('{').Append(nl);
                sb.Append(InsertionText(tt, nl));
                sb.Append('}').Append(nl);
                break;
        }
        return sb.ToString();
    }

    private readonly Dictionary<string, List<string>> _createdTypeExtraUsings = new(StringComparer.Ordinal);
    private readonly HashSet<(string Type, string Using)> _createdTypeRemovedUsings = new();

    // Usings of created types that the final compile reports unused (CS8019) are dropped; returns true when something changed.
    private bool PruneCreatedTypeUsings()
    {
        bool changed = false;
        foreach (TargetType tt in _targetTypes.Values.Where(t => t.Created))
        {
            foreach (ProjectModel p in tt.Doc!.Projects)
            {
                if (!_docTreeFinal.TryGetValue((p, tt.Doc), out SyntaxTree? tree))
                {
                    continue;
                }
                foreach (Diagnostic x in _final[p].GetDiagnostics().Where(x => x.Id == "CS8019" && x.Location.SourceTree == tree))
                {
                    UsingDirectiveSyntax? u = tree.GetRoot().FindNode(x.Location.SourceSpan).FirstAncestorOrSelf<UsingDirectiveSyntax>();
                    if (u?.Name != null && _createdTypeRemovedUsings.Add((tt.FullName, u.Name.ToString())))
                    {
                        changed = true;
                    }
                }
            }
        }
        return changed;
    }

    // Position of a site of a moved declaration in the final text of a target doc.
    private IEnumerable<(CsDoc Doc, int Pos)> MovedSitePositions(MovedMember mm, Site s)
    {
        foreach (string t in mm.Targets)
        {
            TargetType tt = _targetTypes[t];
            CsDoc doc = tt.Doc!;
            string final = doc.ViewText;
            string inserted = Reindent(mm.MovedText!, IndentOf(mm.Doc.Text0, mm.Decl.SpanStart), tt.Indent, doc.NewLine);
            int at = final.IndexOf(inserted, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }
            // Offset inside the moved text, then the shift Reindent applied before that point.
            int rel = s.IsEdit ? TextEdits.MapPosition(mm.Inner, s.ReplaceStart - mm.SpanStart) + s.EffectiveAnchor : TextEdits.MapPosition(mm.Inner, s.Start - mm.SpanStart);
            string movedPrefix = mm.MovedText!.Substring(0, Math.Min(rel, mm.MovedText!.Length));
            string reindentedPrefix = ReindentPrefix(movedPrefix, mm.MovedText!, IndentOf(mm.Doc.Text0, mm.Decl.SpanStart), tt.Indent, doc.NewLine);
            yield return (doc, at + reindentedPrefix.Length);
        }
    }

    private static string ReindentPrefix(string prefix, string whole, string fromIndent, string toIndent, string nl)
    {
        // Same transformation as Reindent, applied to a prefix (leading blank lines removed, indentation swapped per line).
        string lead = whole.Replace("\r\n", "\n");
        int skip = 0;
        foreach (string l in lead.Split('\n'))
        {
            if (l.Trim().Length == 0)
            {
                skip += l.Length + 1;
            }
            else
            {
                break;
            }
        }
        string p = prefix.Replace("\r\n", "\n");
        p = p.Length > skip ? p.Substring(skip) : "";
        var lines = p.Split('\n');
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i];
            bool full = i < lines.Length - 1;
            if (l.StartsWith(fromIndent, StringComparison.Ordinal))
            {
                l = toIndent + l.Substring(fromIndent.Length);
            }
            sb.Append(l);
            if (full)
            {
                sb.Append(nl);
            }
        }
        return sb.ToString();
    }
}
