using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RenameApply.CSharp;

// C# part of Wave R: semantic renames, namespace moves, file moves/extraction, using repair, [DefOf] field renames,
// literal and comment handling, and verification (compile diff + binding equivalence) on every project under Source/.
public sealed partial class CSharpPass
{
    private readonly RepoPaths _repo;
    private readonly RenameModel _m;
    private readonly RunReport _report;
    private readonly Options _opt;
    private readonly TextWriter _log;

    public List<ProjectModel> Projects { get; private set; } = new();
    public ProjectModel Main { get; private set; } = null!;
    public List<CsDoc> Docs { get; } = new();
    private readonly Dictionary<string, CsDoc> _docByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _origText = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PortableExecutableReference> _refCache = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<ProjectModel, CSharpCompilation> Comp0 { get; private set; } = new();
    private CSharpCompilation _originalMain = null!;
    private Dictionary<SyntaxTree, CsDoc> _treeDoc0 = new();
    private Dictionary<(ProjectModel, CsDoc), SyntaxTree> _docTree0 = new();
    private readonly Dictionary<ProjectModel, List<string>> _baselineErrors = new();
    private readonly Dictionary<CsDoc, HashSet<string>> _baselineUnusedUsings = new();

    private readonly Dictionary<CsDoc, DocPlan> _plans = new();
    private readonly Dictionary<ProjectModel, HashSet<string>> _globalUsings = new();
    private readonly HashSet<string> _newNamespaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _defOfFieldRenames = new(StringComparer.Ordinal);   // old field doc id -> new name
    private readonly Dictionary<string, string> _defOfMappedKeys = new(StringComparer.Ordinal);     // mapped old doc id -> new doc id

    public Dictionary<string, string> FileMoves { get; } = new(StringComparer.OrdinalIgnoreCase);   // rel old -> rel new (C#)
    public List<(string OutFull, string Text, bool Bom, string? DeleteFull)> Writes { get; } = new();
    public List<string> Deletes { get; } = new();                                                    // full paths emptied by the run
    public bool VerificationFailed { get; private set; }

    public CSharpPass(RepoPaths repo, RenameModel model, RunReport report, Options opt, TextWriter log)
    {
        _repo = repo;
        _m = model;
        _report = report;
        _opt = opt;
        _log = log;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Loading and compiling
    // ------------------------------------------------------------------------------------------------------------------

    public void Load()
    {
        Projects = MsBuildProjects.Load(_repo.Full("Source"), _opt.MsBuildProperties, _log);
        string mainDir = _repo.Full("Source/MugirlRace") + Path.DirectorySeparatorChar;
        Main = Projects.FirstOrDefault(p => string.Equals(p.Directory, mainDir, StringComparison.OrdinalIgnoreCase))
               ?? throw new ToolException("Source/MugirlRace/*.csproj not found");
        foreach (ProjectModel p in Projects)
        {
            foreach (string src in p.SourceFiles)
            {
                if (!_docByKey.TryGetValue(src, out CsDoc? doc))
                {
                    if (!File.Exists(src))
                    {
                        throw new ToolException($"{p.Name}: source file missing on disk: {src}");
                    }
                    TextFileData data = TextFileData.Read(src);
                    string rel = _repo.Rel(src);
                    doc = new CsDoc
                    {
                        Key = src,
                        OrigFull = src,
                        OutFull = src,
                        Text0 = data.Text,
                        Bom = data.Bom,
                        NewLine = data.NewLine,
                        Editable = _repo.IsInside(src) && RepoPaths.IsEditable(rel),
                    };
                    _docByKey[src] = doc;
                    _origText[src] = data.Text;
                    Docs.Add(doc);
                }
                doc.Projects.Add(p);
            }
        }
        _log.WriteLine($"  {Projects.Count} projects, {Docs.Count} C# files ({Docs.Count(d => d.Editable)} editable)");
    }

    private PortableExecutableReference GetRef(string path, MetadataReferenceProperties props)
    {
        if (!_refCache.TryGetValue(path, out PortableExecutableReference? r))
        {
            r = MetadataReference.CreateFromFile(path);
            _refCache[path] = r;
        }
        return props.Equals(default(MetadataReferenceProperties)) || props.Equals(MetadataReferenceProperties.Assembly) ? r : r.WithProperties(props);
    }

    private IEnumerable<CsDoc> DocsOf(ProjectModel p, bool final = false)
    {
        List<CsDoc> eligible = Docs.Where(d => d.Projects.Contains(p) && (final || !d.FinalOnly)).ToList();
        var emitted = new HashSet<CsDoc>();
        foreach (string source in p.SourceFiles)
        {
            foreach (CsDoc doc in eligible.Where(d => string.Equals(d.OrderSource ?? d.OrigFull, source, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(d => d.OrderPosition))
            {
                if (emitted.Add(doc))
                {
                    yield return doc;
                }
            }
        }
        // 真正新增的文件没有旧位置，保留创建顺序；下次运行会从 MSBuild 读到它们的实际位置。
        foreach (CsDoc doc in eligible.Where(emitted.Add))
        {
            yield return doc;
        }
    }

    public IReadOnlyList<string> FinalCompileOrder()
        => DocsOf(Main, final: true)
            .Where(d => !RepoPaths.HasSegment(d.OutFull, "bin") && !RepoPaths.HasSegment(d.OutFull, "obj")
                && !d.Emptied && !Deletes.Contains(d.OrigFull ?? "", StringComparer.OrdinalIgnoreCase))
            .Select(d => d.OutFull).ToList();

    private Dictionary<ProjectModel, CSharpCompilation> BuildAll(bool final, out Dictionary<SyntaxTree, CsDoc> treeDoc, out Dictionary<(ProjectModel, CsDoc), SyntaxTree> docTree)
    {
        var result = new Dictionary<ProjectModel, CSharpCompilation>();
        treeDoc = new Dictionary<SyntaxTree, CsDoc>();
        docTree = new Dictionary<(ProjectModel, CsDoc), SyntaxTree>();
        var byPath = Projects.ToDictionary(p => p.ProjectPath, StringComparer.OrdinalIgnoreCase);
        foreach (ProjectModel p in Projects)
        {
            var trees = new List<SyntaxTree>();
            foreach (CsDoc d in DocsOf(p, final))
            {
                string text = final ? d.ViewText : d.Text0;
                string path = final ? d.OutFull : (d.OrigFull ?? d.OutFull);
                SyntaxTree t = CSharpSyntaxTree.ParseText(SourceText.From(text, Encoding.UTF8), p.ParseOptions, path);
                trees.Add(t);
                treeDoc[t] = d;
                docTree[(p, d)] = t;
            }
            var refs = new List<MetadataReference>();
            refs.AddRange(p.MetadataRefs.Select(r => (MetadataReference)GetRef(r.Path, r.Props)));
            foreach (var (proj, props) in p.ProjectRefs)
            {
                refs.Add(result[byPath[proj]].ToMetadataReference(props.Aliases, props.EmbedInteropTypes));
            }
            result[p] = Generators.Run(CSharpCompilation.Create(p.AssemblyName, trees, refs, p.CompilationOptions), p, _log);
        }
        return result;
    }

    private static string ErrorSignature(ProjectModel p, Diagnostic d) =>
        $"{p.Name}|{d.Id}|{d.GetMessage(CultureInfo.InvariantCulture)}";

    private static string ErrorDisplay(Diagnostic d, Func<SyntaxTree, string> fileOf)
    {
        string where = d.Location.IsInSource
            ? $"{fileOf(d.Location.SourceTree!)}:{d.Location.GetLineSpan().StartLinePosition.Line + 1}"
            : "(no location)";
        return $"{where} {d.Id} {d.GetMessage(CultureInfo.InvariantCulture)}";
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Entry point
    // ------------------------------------------------------------------------------------------------------------------

    public void Run()
    {
        AddCreatedFiles();
        Phase0Split();
        _log.WriteLine("  compiling baseline...");
        Comp0 = BuildAll(final: false, out _treeDoc0, out _docTree0);
        _originalMain = Comp0[Main];
        foreach (ProjectModel p in Projects)
        {
            var errs = Comp0[p].GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            _baselineErrors[p] = errs.Select(d => ErrorSignature(p, d)).ToList();
            var res = new ProjectCompileResult { Project = p.Name, BaselineErrors = errs.Count };
            res.BaselineErrorSamples.AddRange(errs.Take(20).Select(d => ErrorDisplay(d, t => _repo.Rel(t.FilePath))));
            _report.Compile.Add(res);
            if (errs.Count > 0)
            {
                _report.Warn("baseline-errors", $"{p.Name} has {errs.Count} compile errors before renaming (compared, not fixed)", _repo.Rel(p.ProjectPath));
            }
        }
        if (_extractions.Count > 0)
        {
            CheckPhase0AgainstOriginal();
        }
        CollectBaselineUnusedUsings();
        BuildTypeTables();
        BuildMemberRenames();
        BuildDefOfMap();
        BuildMemberOps();
        BuildTables();
        BuildNamespaceUniverse();
        ComputeFileTargets();
        _log.WriteLine("  planning...");
        foreach (CsDoc d in Docs.Where(d => d.Editable && !d.FinalOnly))
        {
            _plans[d] = new DocPlan { Doc = d };
        }
        PlanNamespaces();
        foreach (ProjectModel p in Projects)
        {
            CollectGlobalUsings(p);
        }
        foreach (ProjectModel p in Projects)
        {
            foreach (CsDoc d in DocsOf(p).Where(d => d.Editable && _plans.ContainsKey(d)))
            {
                Walk(p, d, _docTree0[(p, d)], Comp0[p].GetSemanticModel(_docTree0[(p, d)]), primary: d.Projects[0] == p);
            }
        }
        foreach (DocPlan plan in _plans.Values)
        {
            RemoveCoveredSites(plan);
        }
        EvaluateRequirements();
        VerifyLoop();
        if (!VerificationFailed)
        {
            CleanupUsings();
        }
        ReportEdits();
        CollectWrites();
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Type tables
    // ------------------------------------------------------------------------------------------------------------------

    private static string NsOf(INamedTypeSymbol t)
    {
        INamedTypeSymbol o = t;
        while (o.ContainingType != null)
        {
            o = o.ContainingType;
        }
        return o.ContainingNamespace == null || o.ContainingNamespace.IsGlobalNamespace ? "" : o.ContainingNamespace.ToDisplayString();
    }

    private static List<string> ChainOf(INamedTypeSymbol t)
    {
        var chain = new List<string>();
        for (INamedTypeSymbol? c = t; c != null; c = c.ContainingType)
        {
            chain.Insert(0, c.MetadataName);
        }
        return chain;
    }

    public static string MetadataKey(INamedTypeSymbol t)
    {
        t = t.OriginalDefinition;
        string ns = NsOf(t);
        return (ns.Length > 0 ? ns + "." : "") + string.Join("+", ChainOf(t));
    }

    private static string DocForm(INamedTypeSymbol t)
    {
        t = t.OriginalDefinition;
        string ns = NsOf(t);
        return (ns.Length > 0 ? ns + "." : "") + string.Join(".", ChainOf(t));
    }

    // Types the spec may rename: the mod and its unit tests (test classes follow their subject). Validator negative controls
    // declare look-alike Mugirl types on purpose and are never renamed.
    private List<string>? _ownedDirs;

    private List<string> OwnedDirectories => _ownedDirs ??= Projects
        .Where(p => p == Main || p.Name == "MugirlRace.Tests")
        .Select(p => p.Directory).ToList();

    private bool IsOwned(INamedTypeSymbol t)
    {
        INamedTypeSymbol o = t.OriginalDefinition;
        while (o.ContainingType != null)
        {
            o = o.ContainingType;
        }
        bool anySource = false;
        foreach (Location loc in o.Locations)
        {
            if (loc.IsInSource && loc.SourceTree != null)
            {
                anySource = true;
                if (OwnedDirectories.Any(d => loc.SourceTree.FilePath.StartsWith(d, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        return !anySource && o.ContainingAssembly?.Name == Main.AssemblyName;
    }

    private TypeRename? Lookup(INamedTypeSymbol t)
    {
        if (t.TypeKind == TypeKind.Error)
        {
            return null;
        }
        return _m.TypesByOldMetadata.TryGetValue(MetadataKey(t), out TypeRename? tr) && IsOwned(t) ? tr : null;
    }

    // Any owned type whose own name or an outer type's name changes.
    private bool Moves(INamedTypeSymbol t) => IsOwned(t) && _m.MapDocString(DocForm(t)) != DocForm(t);

    private string NewCSharpName(INamedTypeSymbol t) => Regex.Replace(_m.MapDocString(DocForm(t)), "`\\d+", "");

    private string NewNamespaceOf(INamedTypeSymbol t)
    {
        INamedTypeSymbol o = t.OriginalDefinition;
        while (o.ContainingType != null)
        {
            o = o.ContainingType;
        }
        string mapped = _m.MapDocString(DocForm(o));
        int dot = mapped.LastIndexOf('.');
        return dot < 0 ? "" : mapped.Substring(0, dot);
    }

    private void BuildTypeTables()
    {
        CSharpCompilation main = Comp0[Main];
        foreach (TypeRename tr in _m.Types)
        {
            _m.AddDocName(tr.Old.DocForm, tr.New.DocForm);
        }
        foreach (TypeRename tr in _m.Types)
        {
            INamedTypeSymbol? sym = main.GetTypeByMetadataName(tr.Old.Metadata)
                ?? Projects.Where(p => p != Main && OwnedDirectories.Contains(p.Directory)).Select(p => Comp0[p].GetTypeByMetadataName(tr.Old.Metadata)).FirstOrDefault(s => s != null && IsOwned(s));
            if (sym == null)
            {
                _report.Error("spec-type-missing", $"type {tr.Old.Metadata} ({tr.Entry.Module}) not found in {Main.Name}");
                continue;
            }
            if (!IsOwned(sym))
            {
                _report.Error("spec-type-foreign", $"type {tr.Old.Metadata} is not declared in Source/MugirlRace");
                continue;
            }
            INamedTypeSymbol? clash = main.GetTypeByMetadataName(tr.New.Metadata);
            if (clash != null && !_m.TypesByOldMetadata.ContainsKey(tr.New.Metadata) && tr.New.Metadata != tr.Old.Metadata)
            {
                _report.Error("spec-type-clash", $"type {tr.Old.Metadata} -> {tr.New.Metadata}: target already exists");
            }
            var files = sym.DeclaringSyntaxReferences.Select(r => r.SyntaxTree.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (tr.Entry.OldFile != null && !files.Any(f => string.Equals(f, _repo.Full(tr.Entry.OldFile), StringComparison.OrdinalIgnoreCase)
                                                       || _extractions.Any(x => string.Equals(x.Doc.OutFull, f, StringComparison.OrdinalIgnoreCase) && string.Equals(x.FromRel, RepoPaths.Norm(tr.Entry.OldFile), StringComparison.OrdinalIgnoreCase))))
            {
                _report.Error("spec-oldfile", $"type {tr.Old.Metadata}: oldFile {tr.Entry.OldFile} does not declare it (declared in {string.Join(", ", files.Select(_repo.Rel))})");
            }
            if (tr.Old.Metadata == tr.New.Metadata)
            {
                continue;
            }
            _report.Maps.Types.Add(new TypeMapEntry
            {
                Old = tr.Old.Metadata, New = tr.New.Metadata, SaveVisible = tr.Entry.SaveVisible, XmlVisible = tr.Entry.XmlVisible, Module = tr.Entry.Module,
                Transitional = tr.Entry.Transitional is { ValueKind: System.Text.Json.JsonValueKind.Object } t ? t : null,
                PublicAlias = tr.Entry.PublicAlias is { ValueKind: System.Text.Json.JsonValueKind.Object } a && a.TryGetProperty("name", out var an) ? an.GetString() : null,
            });
        }
        // Nested types that move with their outer type (not listed in the spec but their full names change).
        foreach (INamedTypeSymbol t in AllSourceTypes(main.Assembly.GlobalNamespace))
        {
            if (t.ContainingType == null || !IsOwned(t) || _m.TypesByOldMetadata.ContainsKey(MetadataKey(t)))
            {
                continue;
            }
            string oldDoc = DocForm(t);
            string newDoc = _m.MapDocString(oldDoc);
            if (newDoc != oldDoc)
            {
                string oldMeta = MetadataKey(t);
                string newMeta = ToMetadataFromDoc(newDoc, ChainOf(t).Count);
                _report.Maps.Types.Add(new TypeMapEntry { Old = oldMeta, New = newMeta, Implicit = true });
            }
        }
        _report.Maps.Types = _report.Maps.Types.OrderBy(t => t.Old, StringComparer.Ordinal).ToList();

        // design.md 3.1: nothing may stay in the root namespace once the merged spec is applied.
        var remaining = AllSourceTypes(main.Assembly.GlobalNamespace)
            .Where(t => t.ContainingType == null && IsOwned(t) && NsOf(t) == "Mugirl" && !Moves(t) && !t.IsImplicitlyDeclared)
            .Select(t => MetadataKey(t)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (remaining.Count > 0)
        {
            _report.Warn("root-namespace-remaining", $"{remaining.Count} types stay in the root namespace 'Mugirl' after this spec: {string.Join(", ", remaining.Take(40))}{(remaining.Count > 40 ? ", ..." : "")}");
        }
    }

    private static string ToMetadataFromDoc(string doc, int chainLength)
    {
        // The last chainLength segments are the nesting chain.
        string[] parts = doc.Split('.');
        string ns = string.Join(".", parts.Take(parts.Length - chainLength));
        return (ns.Length > 0 ? ns + "." : "") + string.Join("+", parts.Skip(parts.Length - chainLength));
    }

    private static IEnumerable<INamedTypeSymbol> AllSourceTypes(INamespaceSymbol ns)
    {
        foreach (INamespaceOrTypeSymbol m in ns.GetMembers())
        {
            if (m is INamespaceSymbol n)
            {
                foreach (INamedTypeSymbol t in AllSourceTypes(n))
                {
                    yield return t;
                }
            }
            else if (m is INamedTypeSymbol t)
            {
                foreach (INamedTypeSymbol x in WithNested(t))
                {
                    yield return x;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol t)
    {
        yield return t;
        foreach (INamedTypeSymbol n in t.GetTypeMembers())
        {
            foreach (INamedTypeSymbol x in WithNested(n))
            {
                yield return x;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // [DefOf] fields
    // ------------------------------------------------------------------------------------------------------------------

    public DefTypeResolver CreateDefTypeResolver() => new(Comp0[Main], _m);

    // Private fields of engine types load from XML too: the schema needs a compilation that imports non-public metadata.
    public RenameApply.Xml.XmlSchema CreateXmlSchema()
    {
        CSharpCompilation all = Comp0[Main].WithOptions(Comp0[Main].Options.WithMetadataImportOptions(MetadataImportOptions.All));
        return new(all, new DefTypeResolver(all, _m), Main.AssemblyName);
    }

    private void BuildDefOfMap()
    {
        DefTypeResolver resolver = CreateDefTypeResolver();
        foreach (DefRename dr in _m.Defs)
        {
            dr.Root ??= resolver.RootOf(dr.DefType);
            if (dr.Root == null)
            {
                _report.Warn("def-type-unresolved", $"def type '{dr.DefType}' of {dr.Old} not found in the compilation; matching by element name only");
            }
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProjectModel p in Projects)
        {
            foreach (INamedTypeSymbol t in AllSourceTypes(Comp0[p].Assembly.GlobalNamespace))
            {
                if (!IsOwned(t) || !t.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "RimWorld.DefOf"))
                {
                    continue;
                }
                foreach (IFieldSymbol f in t.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsStatic))
                {
                    string? root = DefTypeResolver.RootOf(f.Type);
                    string? alias = DefAliasOf(f);
                    string bound = alias ?? f.Name;
                    var matches = _m.Defs.Where(d => d.Old == bound && (d.Root == null || root == null || d.Root == root)).ToList();
                    if (matches.Count == 0)
                    {
                        continue;
                    }
                    if (matches.Count > 1)
                    {
                        _report.Error("defof-ambiguous", $"[DefOf] field {t.ToDisplayString()}.{f.Name} matches several def renames ({string.Join(", ", matches.Select(d => d.DefType))})");
                        continue;
                    }
                    DefRename dr = matches[0];
                    string oldId = f.GetDocumentationCommentId()!;
                    if (!seen.Add(oldId))
                    {
                        continue;
                    }
                    if (alias != null)
                    {
                        // The field keeps its name; the attribute argument carries the defName.
                        RecordDefAliasLiteral(f, dr);
                        continue;
                    }
                    if (t.GetMembers(dr.New).Any() && !PendingRenameAway(t, dr.New))
                    {
                        _report.Error("defof-clash", $"[DefOf] {t.ToDisplayString()} already has a member named {dr.New}");
                        continue;
                    }
                    _defOfFieldRenames[oldId] = dr.New;
                    string mappedOld = _m.MapDocString(oldId);
                    int lastDot = mappedOld.LastIndexOf('.');
                    _defOfMappedKeys[mappedOld] = mappedOld.Substring(0, lastDot + 1) + dr.New;
                    _report.Maps.DefOfFields.Add(new NameMapEntry { Old = oldId.Substring(2), New = _defOfMappedKeys[mappedOld].Substring(2), Module = dr.Entry.Module });
                }
            }
        }
    }

    private string? MapKey(string? key, ProjectModel p)
    {
        if (key == null)
        {
            return null;
        }
        string mapped = _m.MapDocString(key, Foreign(p));
        return _defOfMappedKeys.TryGetValue(mapped, out string? f) ? f : mapped;
    }

    // Old names that, in this project's compilation, denote a type the project declares itself (not the mod's type).
    private readonly Dictionary<ProjectModel, HashSet<string>> _foreign = new();

    private IReadOnlySet<string> Foreign(ProjectModel p)
    {
        if (!_foreign.TryGetValue(p, out HashSet<string>? set))
        {
            set = new HashSet<string>(StringComparer.Ordinal);
            if (Comp0.TryGetValue(p, out CSharpCompilation? c))
            {
                foreach (TypeRename tr in _m.Types)
                {
                    INamedTypeSymbol? sym = c.GetTypeByMetadataName(tr.Old.Metadata);
                    if (sym != null && !IsOwned(sym))
                    {
                        set.Add(tr.Old.DocForm);
                    }
                }
            }
            _foreign[p] = set;
        }
        return set;
    }

    private static string? KeyOf(ISymbol? s)
    {
        switch (s)
        {
            case null:
                return null;
            case IAliasSymbol a:
                return "alias:" + a.Name;
            case INamespaceSymbol ns:
                return "N:" + (ns.IsGlobalNamespace ? "" : ns.ToDisplayString());
            case ILocalSymbol or IParameterSymbol or IRangeVariableSymbol or ILabelSymbol or ITypeParameterSymbol or IDiscardSymbol or IPreprocessingSymbol:
                return s.Kind + ":" + s.Name;
            case IMethodSymbol { MethodKind: MethodKind.LocalFunction }:
                return "localfn:" + s.Name;
            case ITypeSymbol { TypeKind: TypeKind.Error }:
                return null;
        }
        if (s is IMethodSymbol m && m.ReducedFrom != null)
        {
            s = m.ReducedFrom;
        }
        s = s.OriginalDefinition;
        return s.GetDocumentationCommentId() ?? (s.Kind + ":" + s.ToDisplayString());
    }

    // Verification binding: an ambiguous name binds to nothing (a candidate would hide CS0104-like ambiguity in crefs).
    private static ISymbol? BindStrict(SemanticModel model, SyntaxNode node)
    {
        SymbolInfo info = model.GetSymbolInfo(node);
        if (info.Symbol != null)
        {
            return info.Symbol;
        }
        return info.CandidateReason == CandidateReason.Ambiguous || info.CandidateSymbols.Length == 0 ? null : info.CandidateSymbols[0];
    }

    private static bool IsAmbiguous(SemanticModel model, SyntaxNode node)
    {
        SymbolInfo info = model.GetSymbolInfo(node);
        return info.Symbol == null && info.CandidateReason == CandidateReason.Ambiguous;
    }

    private static ISymbol? Bind(SemanticModel model, SyntaxNode node)
    {
        SymbolInfo info = model.GetSymbolInfo(node);
        return info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Namespaces
    // ------------------------------------------------------------------------------------------------------------------

    private void BuildNamespaceUniverse()
    {
        foreach (ProjectModel p in Projects)
        {
            CSharpCompilation c = Comp0[p];
            foreach (MetadataReference r in c.References)
            {
                if (c.GetAssemblyOrModuleSymbol(r) is IAssemblySymbol a)
                {
                    if (a.Name == Main.AssemblyName && p != Main && p.ProjectRefs.Any(x => string.Equals(x.ProjectPath, Main.ProjectPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue; // source namespaces of the mod come from the mapped source types below
                    }
                    AddNamespaces(a.GlobalNamespace, "");
                }
            }
            foreach (INamedTypeSymbol t in AllSourceTypes(c.Assembly.GlobalNamespace))
            {
                if (t.ContainingType != null)
                {
                    continue;
                }
                string ns = Moves(t) ? NewNamespaceOf(t) : NsOf(t);
                for (string x = ns; x.Length > 0; x = x.Contains('.') ? x.Substring(0, x.LastIndexOf('.')) : "")
                {
                    _newNamespaces.Add(x);
                }
            }
        }
    }

    private void AddNamespaces(INamespaceSymbol ns, string prefix)
    {
        foreach (INamespaceSymbol n in ns.GetNamespaceMembers())
        {
            string full = prefix.Length > 0 ? prefix + "." + n.Name : n.Name;
            _newNamespaces.Add(full);
            AddNamespaces(n, full);
        }
    }

    private void PlanNamespaces()
    {
        foreach (DocPlan plan in _plans.Values)
        {
            CsDoc d = plan.Doc;
            ProjectModel p = d.Projects.Contains(Main) ? Main : d.Projects[0];
            SyntaxTree tree = _docTree0[(p, d)];
            SemanticModel model = Comp0[p].GetSemanticModel(tree);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
            foreach (BaseNamespaceDeclarationSyntax ns in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            {
                var types = ns.Members.Where(m => m is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax).ToList();
                string oldNs = model.GetDeclaredSymbol(ns)?.ToDisplayString() ?? ns.Name.ToString();
                var targets = new SortedSet<string>(StringComparer.Ordinal);
                var movers = new List<string>();
                foreach (MemberDeclarationSyntax m in types)
                {
                    if (model.GetDeclaredSymbol(m) is not INamedTypeSymbol sym)
                    {
                        continue;
                    }
                    string target = Moves(sym) ? NewNamespaceOf(sym) : oldNs;
                    targets.Add(target);
                    movers.Add($"{sym.Name}->{target}");
                }
                bool hasNested = ns.Members.OfType<BaseNamespaceDeclarationSyntax>().Any();
                if (targets.Count > 1)
                {
                    _report.Error("namespace-split-required", $"declares types with different target namespaces ({string.Join(", ", movers)}); give the movers their own newFile", _repo.Rel(d.OutFull));
                    continue;
                }
                if (targets.Count == 1 && targets.Min != oldNs)
                {
                    if (hasNested || ns.Parent is BaseNamespaceDeclarationSyntax)
                    {
                        _report.Error("namespace-nested", "nested namespace declarations are not supported for moved types", _repo.Rel(d.OutFull));
                        continue;
                    }
                    plan.NamespaceDecls.Add((ns.Name.SpanStart, ns.Name.Span.Length, targets.Min!, oldNs));
                }
            }
            // Using directives (all levels), for scope evaluation and cleanup.
            foreach (UsingDirectiveSyntax u in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                if (u.Name == null)
                {
                    continue;
                }
                TextLine line = tree.GetText().Lines.GetLineFromPosition(u.SpanStart);
                TextLine lastLine = tree.GetText().Lines.GetLineFromPosition(u.Span.End);
                bool plain = u.Alias == null && u.StaticKeyword.IsKind(SyntaxKind.None) && u.GlobalKeyword.IsKind(SyntaxKind.None);
                var info = new UsingInfo
                {
                    LineStart = line.Start,
                    LineEnd = lastLine.EndIncludingLineBreak,
                    Start = u.SpanStart,
                    NamespaceName = (plain ? "" : "#") + u.Name.ToString(),
                    InsideNamespace = u.Parent is BaseNamespaceDeclarationSyntax,
                };
                if (plain && model.GetSymbolInfo(u.Name).Symbol is INamespaceSymbol nsSym)
                {
                    info.ResolvedNamespace = nsSym.ToDisplayString();
                }
                plan.Usings.Add(info);
            }
        }
    }

    private void CollectGlobalUsings(ProjectModel p)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (SyntaxTree t in Comp0[p].SyntaxTrees)
        {
            SemanticModel? model = null;
            foreach (UsingDirectiveSyntax u in t.GetCompilationUnitRoot().Usings)
            {
                if (u.GlobalKeyword.IsKind(SyntaxKind.None) || u.Alias != null || !u.StaticKeyword.IsKind(SyntaxKind.None) || u.Name == null)
                {
                    continue;
                }
                model ??= Comp0[p].GetSemanticModel(t);
                if (model.GetSymbolInfo(u.Name).Symbol is INamespaceSymbol ns)
                {
                    set.Add(ns.ToDisplayString());
                }
            }
        }
        _globalUsings[p] = set;
    }

    // Namespace that encloses a position after the rewrite, innermost first, ending with the global namespace "".
    private List<string> NewChainAt(DocPlan plan, CompilationUnitSyntax root, int pos)
    {
        BaseNamespaceDeclarationSyntax? ns = root.FindToken(pos).Parent?.AncestorsAndSelf().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        var names = new List<string>();
        for (BaseNamespaceDeclarationSyntax? n = ns; n != null; n = n.Parent as BaseNamespaceDeclarationSyntax)
        {
            var planned = plan.NamespaceDecls.FirstOrDefault(x => x.Start == n.Name.SpanStart);
            names.Insert(0, planned.NewName ?? n.Name.ToString());
        }
        string full = string.Join(".", names);
        var chain = new List<string>();
        for (string x = full; x.Length > 0; x = x.Contains('.') ? x.Substring(0, x.LastIndexOf('.')) : "")
        {
            chain.Add(x);
        }
        chain.Add("");
        return chain;
    }
}
