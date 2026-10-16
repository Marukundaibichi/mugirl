using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RenameApply.CSharp;

// Planning walk over one (project, file): every simple name becomes a Site (renamed or not) so that requirements,
// qualification fallbacks and the binding-equivalence check share one model.
public sealed partial class CSharpPass
{
    public NameTables Tables { get; private set; } = null!;

    // Short names that are not unique across the mod and every referenced assembly are never rewritten in prose.
    private void BuildTables()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var foreign = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Count(INamespaceSymbol ns, bool isForeign)
        {
            foreach (INamespaceSymbol n in ns.GetNamespaceMembers())
            {
                Count(n, isForeign);
            }
            foreach (INamedTypeSymbol t in ns.GetTypeMembers())
            {
                foreach (INamedTypeSymbol x in WithNested(t))
                {
                    counts[x.Name] = counts.GetValueOrDefault(x.Name) + 1;
                    if (isForeign && x.ContainingType == null && x.DeclaredAccessibility == Accessibility.Public)
                    {
                        if (!foreign.TryGetValue(x.Name, out var list))
                        {
                            foreign[x.Name] = list = new List<string>();
                        }
                        list.Add(x.ToDisplayString());
                    }
                }
            }
        }
        CSharpCompilation main = Comp0[Main];
        Count(main.Assembly.GlobalNamespace, false);
        foreach (MetadataReference r in main.References)
        {
            if (main.GetAssemblyOrModuleSymbol(r) is IAssemblySymbol a && !a.Name.StartsWith("System", StringComparison.Ordinal) && a.Name != "netstandard" && a.Name != "mscorlib"
                && a.Name != "UnityEngine.CoreModule")
            {
                Count(a.GlobalNamespace, true);
            }
        }
        var ambiguous = counts.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
        Tables = NameTables.Build(_m, _report.Maps.Types, ambiguous, Array.Empty<(string, string)>());
        // design.md 3.2 (b): a new short name equal to an engine/HAR/Harmony type forces qualification wherever both are imported.
        var sourceShort = AllSourceTypes(main.Assembly.GlobalNamespace).Where(t => t.ContainingType == null).GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.Select(MetadataKey).ToList());
        foreach (TypeRename tr in _m.Types.Where(t => t.ShortChanges || t.NamespaceChanges))
        {
            if (foreign.TryGetValue(tr.New.Short, out var hits))
            {
                _report.Warn("type-name-collision", $"{tr.Old.Metadata} -> {tr.New.Metadata}: short name equals {string.Join(", ", hits.Take(3))}; every unqualified use next to 'using RimWorld/Verse' gets qualified (consider a Mugirl-specific name, design.md 3.2 b)");
            }
            var mine = (sourceShort.GetValueOrDefault(tr.New.Short) ?? new()).Where(k => k != tr.Old.Metadata && !_m.TypesByOldMetadata.ContainsKey(k)).ToList();
            mine.AddRange(_m.Types.Where(o => o != tr && o.New.Short == tr.New.Short).Select(o => o.New.Metadata));
            if (mine.Count > 0)
            {
                _report.Warn("type-name-collision", $"{tr.Old.Metadata} -> {tr.New.Metadata}: another mod type has the short name {tr.New.Short} ({string.Join(", ", mine.Take(3))})");
            }
        }
    }

    private string RelOf(CsDoc d) => _repo.Rel(d.OrigFull ?? d.OutFull);

    private Site GetOrCreateSite(DocPlan plan, SyntaxToken id)
    {
        if (!plan.Sites.TryGetValue(id.SpanStart, out Site? s))
        {
            s = new Site
            {
                Doc = plan.Doc,
                Start = id.SpanStart,
                OrigText = id.Text,
                ReplaceStart = id.SpanStart,
                ReplaceLength = id.Span.Length,
                NewText = id.Text,
            };
            plan.Sites[id.SpanStart] = s;
        }
        return s;
    }

    private void Walk(ProjectModel p, CsDoc d, SyntaxTree tree, SemanticModel model, bool primary)
    {
        DocPlan plan = _plans[d];
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        foreach (SyntaxNode node in root.DescendantNodes(descendIntoTrivia: true))
        {
            switch (node)
            {
                case BaseTypeDeclarationSyntax td:
                    DeclSite(plan, model.GetDeclaredSymbol(td), td.Identifier, "type-decl");
                    break;
                case DelegateDeclarationSyntax dd:
                    DeclSite(plan, model.GetDeclaredSymbol(dd), dd.Identifier, "type-decl");
                    break;
                case ConstructorDeclarationSyntax cd:
                    DeclSite(plan, model.GetDeclaredSymbol(cd)?.ContainingType, cd.Identifier, "ctor-decl");
                    break;
                case DestructorDeclarationSyntax de:
                    DeclSite(plan, model.GetDeclaredSymbol(de)?.ContainingType, de.Identifier, "ctor-decl");
                    break;
                case VariableDeclaratorSyntax vd when vd.Parent?.Parent is FieldDeclarationSyntax fd:
                    if (model.GetDeclaredSymbol(vd) is IFieldSymbol f && _defOfFieldRenames.TryGetValue(f.GetDocumentationCommentId() ?? "", out string? newField))
                    {
                        Site s = GetOrCreateSite(plan, vd.Identifier);
                        s.Kind = SiteKind.DefOfField;
                        s.NewText = newField;
                        s.EditKind = "defof-field";
                        if (primary && _memberAddAttributes.TryGetValue(f.GetDocumentationCommentId()!, out var attrs))
                        {
                            if (fd.Declaration.Variables.Count > 1)
                            {
                                _report.Error("member-attribute-position", $"{f.Name}: declared together with other fields; split the declaration first", RelOf(d), LinesOf(d).Get(vd.SpanStart).Line);
                            }
                            else
                            {
                                AddMemberAttributes(plan, fd, attrs);
                            }
                        }
                    }
                    break;
                case PropertyDeclarationSyntax pd:
                    MemberDeclSite(plan, model.GetDeclaredSymbol(pd), pd.Identifier, pd, primary);
                    break;
                case MethodDeclarationSyntax md:
                    MemberDeclSite(plan, model.GetDeclaredSymbol(md), md.Identifier, md, primary);
                    break;
                case EventDeclarationSyntax ed:
                    MemberDeclSite(plan, model.GetDeclaredSymbol(ed), ed.Identifier, ed, primary);
                    break;
                case SimpleNameSyntax sn:
                    NameSite(p, plan, model, sn, primary);
                    break;
                case AttributeSyntax attr when primary:
                    AttributeArgumentSite(plan, model, attr);
                    break;
            }
        }
        if (primary)
        {
            Literals(plan, root);
            Comments(plan, root);
            TextEditSites(plan);
        }
    }

    private void MemberDeclSite(DocPlan plan, ISymbol? sym, SyntaxToken id, MemberDeclarationSyntax decl, bool primary)
    {
        string? key = sym?.OriginalDefinition.GetDocumentationCommentId();
        if (key == null || !_defOfFieldRenames.TryGetValue(key, out string? newName))
        {
            return;
        }
        Site s = GetOrCreateSite(plan, id);
        s.Kind = SiteKind.DefOfField;
        s.NewText = newName;
        s.EditKind = "member-decl";
        if (primary && _memberAddAttributes.TryGetValue(key, out var attrs))
        {
            AddMemberAttributes(plan, decl, attrs);
        }
    }

    private static string? MemberKey(ISymbol? s) => s switch
    {
        IFieldSymbol or IPropertySymbol or IEventSymbol => s.OriginalDefinition.GetDocumentationCommentId(),
        IMethodSymbol m => (m.ReducedFrom ?? m).OriginalDefinition.GetDocumentationCommentId(),
        _ => null,
    };

    private void DeclSite(DocPlan plan, ISymbol? sym, SyntaxToken id, string kind)
    {
        if (sym is not INamedTypeSymbol t || Lookup(t) is not TypeRename tr || !tr.ShortChanges)
        {
            return;
        }
        Site s = GetOrCreateSite(plan, id);
        s.Kind = SiteKind.Declaration;
        s.NewText = tr.New.Short;
        s.EditKind = kind;
    }

    private static bool IsLeftmost(SimpleNameSyntax n) => n.Parent switch
    {
        QualifiedNameSyntax q => q.Left == n,
        MemberAccessExpressionSyntax ma => ma.Expression == n,
        AliasQualifiedNameSyntax => false,
        MemberBindingExpressionSyntax => false,
        NameColonSyntax => false,
        NameMemberCrefSyntax nm when nm.Parent is QualifiedCrefSyntax qc && qc.Member == nm => false,
        _ => true,
    };

    private static bool IsAttributeName(SimpleNameSyntax n) =>
        n.Parent is AttributeSyntax || (n.Parent is QualifiedNameSyntax q && q.Right == n && q.Parent is AttributeSyntax);

    private static SyntaxNode? LeftOf(SimpleNameSyntax n) => n.Parent switch
    {
        QualifiedNameSyntax q when q.Right == n => q.Left,
        MemberAccessExpressionSyntax ma when ma.Name == n => ma.Expression,
        // cref="Ns.Type" parses as QualifiedCref(Container: Ns, Member: NameMemberCref(Type)).
        NameMemberCrefSyntax nm when nm.Parent is QualifiedCrefSyntax qc && qc.Member == nm => qc.Container,
        _ => null,
    };

    private static SyntaxNode LeftmostNode(SyntaxNode n)
    {
        while (true)
        {
            switch (n)
            {
                case QualifiedNameSyntax q:
                    n = q.Left;
                    continue;
                case MemberAccessExpressionSyntax ma:
                    n = ma.Expression;
                    continue;
                default:
                    return n;
            }
        }
    }

    // Left part of "Left.Name" denotes a namespace. Inside crefs the semantic model does not bind the left part of a
    // qualified type name, so it is matched against the type's namespace textually there.
    private static bool IsNamespaceQualifier(SemanticModel model, SyntaxNode left, INamedTypeSymbol t)
    {
        if (Bind(model, left) is INamespaceSymbol)
        {
            return true;
        }
        if (left.FirstAncestorOrSelf<CrefSyntax>() == null)
        {
            return false;
        }
        string text = left.ToString().Replace("global::", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        string ns = NsOf(t);
        return text == ns || ns.EndsWith("." + text, StringComparison.Ordinal);
    }

    // Would the first segment of a namespace bind to something else than the global namespace member at this position?
    private static bool FirstSegmentShadowed(SemanticModel model, int pos, string ns, bool expressionContext)
    {
        string first = ns.Split('.')[0];
        var symbols = expressionContext ? model.LookupSymbols(pos, name: first) : model.LookupNamespacesAndTypes(pos, name: first);
        return symbols.Any(s => !(s is INamespaceSymbol n && n.ContainingNamespace != null && n.ContainingNamespace.IsGlobalNamespace));
    }

    private void NameSite(ProjectModel p, DocPlan plan, SemanticModel model, SimpleNameSyntax n, bool primary)
    {
        if (n.Parent is NameEqualsSyntax { Parent: UsingDirectiveSyntax })
        {
            return; // the alias being declared
        }
        if (n.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Any(ns => ns.Name.Span.Contains(n.Span)))
        {
            return; // namespace declaration names are planned separately
        }
        bool inUsing = n.FirstAncestorOrSelf<UsingDirectiveSyntax>() != null;
        SyntaxToken id = n.Identifier;
        ISymbol? sym = Bind(model, n);
        if (sym is IMethodSymbol { MethodKind: MethodKind.Constructor } ctor && IsAttributeName(n))
        {
            sym = ctor.ContainingType;
        }
        IAliasSymbol? alias = n is IdentifierNameSyntax idn ? model.GetAliasInfo(idn) : null;
        Site site = GetOrCreateSite(plan, id);
        if (KeyOf(sym) == null || IsAmbiguous(model, n))
        {
            site.UnresolvedBaselineProjects.Add(p);
        }
        else
        {
            site.UnresolvedBaselineProjects.Remove(p);
        }
        bool leftmost = IsLeftmost(n);
        if (!inUsing && MovedMemberSite(p, plan, model, n, sym, site, primary))
        {
            return;
        }
        switch (sym)
        {
            case INamedTypeSymbol t when t.TypeKind != TypeKind.Error:
            {
                bool attributeShort = IsAttributeName(n) && id.ValueText + "Attribute" == t.Name;
                if (alias != null || !(id.ValueText == t.Name || attributeShort))
                {
                    break; // var, aliases, nint... : the identifier is not the type's own name
                }
                TypeRename? tr = Lookup(t);
                bool moves = Moves(t);
                string newShort = id.Text;
                if (tr != null && tr.ShortChanges)
                {
                    newShort = attributeShort ? (tr.New.Short.EndsWith("Attribute", StringComparison.Ordinal) ? tr.New.Short[..^"Attribute".Length] : tr.New.Short) : tr.New.Short;
                }
                SyntaxNode? left = LeftOf(n);
                if (site.Kind is SiteKind.Name)
                {
                    site.Kind = SiteKind.TypeRef;
                }
                if (tr != null && t.ContainingType == null && tr.NamespaceChanges && left != null && IsNamespaceQualifier(model, left, t))
                {
                    bool hadGlobal = LeftmostNode(left) is AliasQualifiedNameSyntax aq && aq.Alias.Identifier.ValueText == "global";
                    bool needGlobal = hadGlobal || FirstSegmentShadowed(model, left.SpanStart, tr.New.Namespace, n.Parent is MemberAccessExpressionSyntax);
                    string text = (needGlobal ? "global::" : "") + tr.New.Namespace + "." + newShort;
                    site.Kind = SiteKind.QualifiedType;
                    site.ReplaceStart = left.SpanStart;
                    site.ReplaceLength = id.Span.End - left.SpanStart;
                    site.NewText = text;
                    site.AnchorOffset = text.Length - newShort.Length;
                    site.EditKind = "type-ref-qualified";
                }
                else if (newShort != id.Text)
                {
                    site.NewText = newShort;
                    site.EditKind = "type-ref";
                }
                if (leftmost && !inUsing && t.ContainingType == null)
                {
                    string needNs = moves ? NewNamespaceOf(t) : NsOf(t);
                    if (needNs.Length > 0)
                    {
                        plan.Requirements.Add(new Requirement { Project = p, Site = site, Namespace = needNs, Kind = RequirementKind.Type });
                        site.QualifiedText = needNs + "." + newShort;
                        site.QualifyNeedsGlobal = FirstSegmentShadowed(model, id.SpanStart, needNs, !SyntaxFacts.IsInTypeOnlyContext(n));
                    }
                }
                if (primary && tr != null && tr.ShortChanges)
                {
                    ReportNameofAndTypeofName(plan, n, tr);
                }
                break;
            }
            case IFieldSymbol or IPropertySymbol or IEventSymbol or IMethodSymbol when MemberKey(sym) is string mk && _defOfFieldRenames.TryGetValue(mk, out string? newMember) && id.ValueText == sym!.Name:
                site.Kind = SiteKind.DefOfField;
                site.NewText = newMember;
                site.EditKind = sym is IFieldSymbol ? "defof-ref" : "member-ref";
                if (primary && IsInNameof(n))
                {
                    _report.Warn("nameof-renamed-member", $"nameof({sym.Name}) now yields '{newMember}'; check it is not persisted", RelOf(plan.Doc), LinesOf(plan.Doc).Get(n.SpanStart).Line, LinesOf(plan.Doc).LineText(LinesOf(plan.Doc).Get(n.SpanStart).Line).Trim());
                }
                break;
            case IMethodSymbol m when m.ReducedFrom != null && !inUsing:
            {
                INamedTypeSymbol cls = m.ReducedFrom.ContainingType;
                string needNs = Moves(cls) ? NewNamespaceOf(cls) : NsOf(cls);
                if (needNs.Length > 0)
                {
                    plan.Requirements.Add(new Requirement { Project = p, Site = site, Namespace = needNs, Kind = RequirementKind.Extension });
                }
                break;
            }
            case INamespaceSymbol ns when leftmost && !inUsing && !ns.IsGlobalNamespace:
                site.Kind = SiteKind.NamespaceLeft;
                site.BoundNamespace = ns.ToDisplayString();
                site.QualifiedText = site.BoundNamespace;
                site.QualifyNeedsGlobal = FirstSegmentShadowed(model, id.SpanStart, site.BoundNamespace, !SyntaxFacts.IsInTypeOnlyContext(n));
                break;
        }
        if (!inUsing && site.ExpectedKey == null && !IsAmbiguous(model, n))
        {
            // Member renames/moves apply to the mod's own members only (negative controls declare look-alikes).
            bool foreignMember = sym is IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol && sym.ContainingType != null && !IsOwned(sym.ContainingType);
            site.ExpectedKey = foreignMember ? _m.MapDocString(KeyOf(sym) ?? "", Foreign(p)) : MapKey(KeyOf(sym), p);
            if (site.ExpectedKey?.Length == 0)
            {
                site.ExpectedKey = null;
            }
        }
    }

    private static bool IsInNameof(SimpleNameSyntax n)
    {
        SyntaxNode top = n;
        while (top.Parent is QualifiedNameSyntax q && q.Right == top || top.Parent is MemberAccessExpressionSyntax ma && ma.Name == top)
        {
            top = top.Parent!;
        }
        return top.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax inv } } && inv.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" };
    }

    private void ReportNameofAndTypeofName(DocPlan plan, SimpleNameSyntax n, TypeRename tr)
    {
        var lines = LinesOf(plan.Doc);
        SyntaxNode top = n;
        while (top.Parent is QualifiedNameSyntax q && q.Right == top || top.Parent is MemberAccessExpressionSyntax ma && ma.Name == top)
        {
            top = top.Parent!;
        }
        if (top.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax inv } } && inv.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" })
        {
            _report.Warn("nameof-renamed-type", $"nameof({tr.Old.Short}) now yields \"{tr.New.Short}\"; check it is not persisted (Scribe label, translation key, settings)", RelOf(plan.Doc), lines.Get(n.SpanStart).Line, lines.LineText(lines.Get(n.SpanStart).Line).Trim());
        }
        if (top.Parent is TypeOfExpressionSyntax tof && tof.Parent is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Name" or "FullName" or "Namespace" })
        {
            _report.Warn("typeof-name-renamed-type", $"typeof({tr.Old.Short}).Name/FullName changes value; check it is not persisted", RelOf(plan.Doc), lines.Get(n.SpanStart).Line, lines.LineText(lines.Get(n.SpanStart).Line).Trim());
        }
    }

    private readonly Dictionary<CsDoc, LineIndex> _lines0 = new();

    private LineIndex LinesOf(CsDoc d)
    {
        if (!_lines0.TryGetValue(d, out LineIndex? li))
        {
            _lines0[d] = li = new LineIndex(d.Text0);
        }
        return li;
    }

    private void RemoveCoveredSites(DocPlan plan)
    {
        var qualified = plan.Sites.Values.Where(s => s.Kind == SiteKind.QualifiedType).ToList();
        foreach (Site q in qualified)
        {
            foreach (Site s in plan.Sites.Values)
            {
                if (s != q && s.Start >= q.ReplaceStart && s.Start < q.ReplaceStart + q.ReplaceLength)
                {
                    s.Removed = true;
                }
            }
        }
        plan.Requirements.RemoveAll(r => r.Site.Removed);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // String literals
    // ------------------------------------------------------------------------------------------------------------------

    private bool LiteralExcluded(string rel) => _opt.LiteralExcludes.Any(g => Glob.IsMatch(g, rel));

    private void Literals(DocPlan plan, CompilationUnitSyntax root)
    {
        string rel = RelOf(plan.Doc);
        bool excluded = LiteralExcluded(rel);
        LineIndex lines = LinesOf(plan.Doc);
        foreach (SyntaxToken tok in root.DescendantTokens())
        {
            bool whole;
            switch (tok.Kind())
            {
                case SyntaxKind.StringLiteralToken:
                case SyntaxKind.Utf8StringLiteralToken:
                case SyntaxKind.SingleLineRawStringLiteralToken:
                case SyntaxKind.MultiLineRawStringLiteralToken:
                case SyntaxKind.Utf8SingleLineRawStringLiteralToken:
                case SyntaxKind.Utf8MultiLineRawStringLiteralToken:
                    whole = true;
                    break;
                case SyntaxKind.InterpolatedStringTextToken:
                    whole = false;
                    break;
                default:
                    continue;
            }
            string value = tok.ValueText;
            if (value.Length == 0)
            {
                continue;
            }
            int line = lines.Get(tok.SpanStart).Line;
            string lineText = lines.LineText(line).Trim();
            if (excluded)
            {
                if (MentionsAnything(value))
                {
                    _report.Info("literal-excluded-zone", "old name in a literal of an excluded zone (left as is)", rel, line, value);
                }
                continue;
            }
            string? newValue = null, kind = null, why = null;
            if (plan.Doc.OrigFull != null && !plan.Doc.Extracted && _aliasLiteralRenames.TryGetValue((plan.Doc.OrigFull, tok.SpanStart), out string? aliasNew))
            {
                plan.TriviaEdits.Add(new TextEdit(tok.SpanStart, tok.Span.Length, tok.Text.Replace(value, aliasNew, StringComparison.Ordinal), "defalias-def"));
                continue;
            }
            if (whole && TryExactLiteral(rel, line, value, out newValue, out kind, out why))
            {
                int i = tok.Text.IndexOf(value, StringComparison.Ordinal);
                if (i >= 0 && i == tok.Text.LastIndexOf(value, StringComparison.Ordinal))
                {
                    string newTok = tok.Text.Substring(0, i) + newValue + tok.Text.Substring(i + value.Length);
                    plan.TriviaEdits.Add(new TextEdit(tok.SpanStart, tok.Span.Length, newTok, kind!));
                }
                else
                {
                    _report.Warn("literal-unreplaceable", $"literal equals {value} but its source text is escaped; replace by hand with {newValue}", rel, line, lineText);
                }
                continue;
            }
            if (why != null)
            {
                _report.Warn("literal-def-shared", why, rel, line, lineText);
                continue;
            }
            if (MemberNames().Contains(value))
            {
                _report.Warn("literal-member-name", $"literal \"{value}\" equals the name of a renamed/moved member (reflection by name?) - not replaced", rel, line, lineText);
            }
            if (Tables.DefByOld.ContainsKey(value) && Tables.IsNotRef(value, rel + ":" + line))
            {
                _report.Info("literal-not-ref", $"literal \"{value}\" is listed in notRefs (left as is)", rel, line);
                continue;
            }
            ReportLiteralMentions(rel, line, value, lineText);
        }
    }

    private bool MentionsAnything(string s) =>
        (Tables.TypeFullRx?.IsMatch(s) ?? false) || (Tables.DefRx?.IsMatch(s) ?? false) || (Tables.KeyRx?.IsMatch(s) ?? false) || (Tables.AbstractRx?.IsMatch(s) ?? false);

    private bool TryExactLiteral(string rel, int line, string value, out string? newValue, out string? kind, out string? why)
    {
        newValue = null;
        kind = null;
        why = null;
        if (Tables.TypeFull.TryGetValue(value, out string? nt))
        {
            newValue = nt;
            kind = "literal-type";
            return true;
        }
        if (Tables.Keys.TryGetValue(value, out string? nk))
        {
            newValue = nk;
            kind = "literal-key";
            return true;
        }
        if (Tables.DefByOld.ContainsKey(value))
        {
            if (Tables.IsNotRef(value, rel + ":" + line))
            {
                return false;
            }
            string? target = Tables.UniqueDefTarget(value, out DefRename? def);
            if (target != null)
            {
                newValue = target;
                kind = "literal-def";
                return true;
            }
            string refKey = rel + ":" + line;
            var hits = Tables.DefByOld[value].Where(x => x.Def.ExplicitRefs.Contains(refKey)).ToList();
            if (hits.Count == 1)
            {
                hits[0].Def.UsedExplicitRefs.Add(refKey);
                newValue = hits[0].New;
                kind = "literal-def";
                return true;
            }
            why = $"literal \"{value}\" names a def that shares its name with another def type ({string.Join(", ", Tables.DefByOld[value].Select(x => x.Def.DefType + "->" + x.New))}); list {refKey} in explicitRefs to rename it";
        }
        return false;
    }

    private HashSet<string>? _keyedKeys;

    // Every Keyed key of the content (all languages): a literal that IS a Keyed key only shares a prefix with a type name.
    private HashSet<string>? _memberNames;

    // Simple names of members that a memberRenames/memberMoves/memberDeletes row changes.
    private HashSet<string> MemberNames() => _memberNames ??= _defOfFieldRenames.Keys.Concat(_moved.Keys)
        .Select(id => id.Substring(2).Split('(')[0]).Select(s => s.Substring(s.LastIndexOf('.') + 1)).ToHashSet(StringComparer.Ordinal);

    public HashSet<string> KeyedKeys()
    {
        if (_keyedKeys != null)
        {
            return _keyedKeys;
        }
        _keyedKeys = new HashSet<string>(StringComparer.Ordinal);
        string root = _repo.Full("1.7");
        foreach (string f in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories))
        {
            string rel = _repo.Rel(f);
            if (!rel.Contains("/Languages/", StringComparison.Ordinal) || !rel.Contains("/Keyed/", StringComparison.Ordinal))
            {
                continue;
            }
            try
            {
                var doc = RenameApply.Xml.XDoc.Parse(TextFileData.Read(f).Text);
                foreach (var c in doc.Root?.Children ?? new List<RenameApply.Xml.XElem>())
                {
                    _keyedKeys.Add(c.Name);
                }
            }
            catch (FormatException)
            {
            }
        }
        return _keyedKeys;
    }

    private void ReportLiteralMentions(string rel, int line, string value, string lineText)
    {
        if (KeyedKeys().Contains(value) && !Tables.Keys.ContainsKey(value))
        {
            if (Tables.TypeFullRx?.IsMatch(value) == true)
            {
                _report.Info("literal-keyed-key", "literal is a Keyed key that shares a prefix with an old type name (keys are not renamed by type renames)", rel, line);
            }
            return;
        }
        if (Tables.TypeFullRx?.IsMatch(value) == true)
        {
            _report.Warn("literal-contains-type", "literal contains an old type name (not replaced)", rel, line, lineText);
        }
        if (Tables.DefRx?.IsMatch(value) == true)
        {
            _report.Warn("literal-contains-def", "literal contains an old defName (not replaced)", rel, line, lineText);
        }
        if (Tables.KeyRx?.IsMatch(value) == true)
        {
            _report.Warn("literal-contains-key", "literal contains an old Keyed key (not replaced)", rel, line, lineText);
        }
        if (Tables.AbstractRx?.IsMatch(value) == true)
        {
            _report.Warn("literal-contains-abstract", "literal contains an old abstract Name (not replaced)", rel, line, lineText);
        }
        if (Tables.TypeShort.ContainsKey(value))
        {
            _report.Warn("literal-short-type", $"literal equals the old short type name {value} (handle, label or reflection?) — not replaced", rel, line, lineText);
        }
        else if (Tables.DistinctiveShortRx?.Match(value) is { Success: true } sm)
        {
            _report.Warn("literal-contains-short-type", $"literal mentions the old short type name {sm.Value} (message text?) — not replaced", rel, line, lineText);
        }
        // Dynamic key composition ("Mugirl.X.Tier" + level, $"Mugirl.CQ.{kind}"): the literal is a proper prefix of renamed keys.
        if (value.Count(c => c == '.') >= 2 && value.Length >= 8)
        {
            var prefixed = Tables.Keys.Keys.Where(k => k.Length > value.Length && k.StartsWith(value, StringComparison.Ordinal)).ToList();
            if (prefixed.Count > 0)
            {
                _report.Warn("literal-key-prefix", $"literal '{value}' is a prefix of {prefixed.Count} renamed Keyed key(s) ({string.Join(", ", prefixed.Take(3))}): dynamic key composition breaks", rel, line, lineText);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Comments
    // ------------------------------------------------------------------------------------------------------------------

    private void Comments(DocPlan plan, CompilationUnitSyntax root)
    {
        string rel = RelOf(plan.Doc);
        LineIndex lines = LinesOf(plan.Doc);
        foreach (SyntaxTrivia tr in root.DescendantTrivia(descendIntoTrivia: true))
        {
            switch (tr.Kind())
            {
                case SyntaxKind.SingleLineCommentTrivia:
                case SyntaxKind.MultiLineCommentTrivia:
                    CommentText(plan, rel, lines, tr.ToString(), tr.SpanStart);
                    break;
                case SyntaxKind.DisabledTextTrivia:
                    if (MentionsAnything(tr.ToString()) || (Tables.TypeShortRx?.IsMatch(tr.ToString()) ?? false))
                    {
                        _report.Warn("disabled-code-mention", "old name inside #if-disabled code (not rewritten)", rel, lines.Get(tr.SpanStart).Line);
                    }
                    break;
            }
        }
        foreach (SyntaxToken tok in root.DescendantTokens(descendIntoTrivia: true))
        {
            if (tok.IsKind(SyntaxKind.XmlTextLiteralToken) && tok.Parent is XmlTextSyntax)
            {
                CommentText(plan, rel, lines, tok.Text, tok.SpanStart);
            }
        }
    }

    private void CommentText(DocPlan plan, string rel, LineIndex lines, string text, int offset)
    {
        var taken = new List<(int S, int E)>();
        if (Tables.TypeFullRx != null)
        {
            foreach (Match mt in Tables.TypeFullRx.Matches(text))
            {
                plan.TriviaEdits.Add(new TextEdit(offset + mt.Index, mt.Length, Tables.TypeFull[mt.Value], "comment-type"));
                taken.Add((mt.Index, mt.Index + mt.Length));
            }
        }
        if (Tables.DistinctiveShortRx != null)
        {
            foreach (Match mt in Tables.DistinctiveShortRx.Matches(text))
            {
                if (taken.Any(t => mt.Index < t.E && mt.Index + mt.Length > t.S))
                {
                    continue;
                }
                plan.TriviaEdits.Add(new TextEdit(offset + mt.Index, mt.Length, Tables.TypeShort[mt.Value], "comment-short-type"));
            }
        }
        if (Tables.TypeShortRx != null)
        {
            foreach (Match mt in Tables.TypeShortRx.Matches(text))
            {
                if (!Tables.DistinctiveShort.Contains(mt.Value) && !taken.Any(t => mt.Index < t.E && mt.Index + mt.Length > t.S))
                {
                    _report.Info("comment-mentions-short-type", $"comment mentions short name {mt.Value} (ambiguous, not rewritten)", rel, lines.Get(offset + mt.Index).Line);
                }
            }
        }
        foreach (var (rx, kind) in new[] { (Tables.DefRx, "comment-mentions-def"), (Tables.KeyRx, "comment-mentions-key"), (Tables.AbstractRx, "comment-mentions-abstract") })
        {
            if (rx == null)
            {
                continue;
            }
            foreach (Match mt in rx.Matches(text))
            {
                _report.Info(kind, $"comment mentions {mt.Value} (not rewritten)", rel, lines.Get(offset + mt.Index).Line);
            }
        }
    }
}
