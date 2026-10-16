using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RenameApply.CSharp;

// Member-level operations: memberRenames (R and R2), created files (verbatim), [DefAlias] arguments that bind renamed defs.
public sealed partial class CSharpPass
{
    private readonly Dictionary<string, List<string>> _memberAddAttributes = new(StringComparer.Ordinal);   // old member doc id -> attributes

    // Instance fields renamed by memberRenames: XML elements (and DefInjected path segments) that load into them follow.
    public List<(string Owner, string Old, string New)> XmlFieldRenames { get; } = new();
    private readonly Dictionary<(string File, int Start), string> _aliasLiteralRenames = new();          // DefAlias("old") literal -> new defName

    // Resolves "Ns.Type.Member" (or "Ns.Type.Member(T1,T2)") in the main compilation; reports and returns null when ambiguous.
    private ISymbol? ResolveMember(string spec, string what)
    {
        string sig = "";
        string s = spec.Trim();
        int paren = s.IndexOf('(');
        if (paren >= 0)
        {
            sig = s.Substring(paren);
            s = s.Substring(0, paren);
        }
        int dot = s.LastIndexOf('.');
        if (dot <= 0)
        {
            _report.Error("spec-member-invalid", $"{what}: '{spec}' is not Ns.Type.Member");
            return null;
        }
        string typeName = s.Substring(0, dot), member = s.Substring(dot + 1);
        INamedTypeSymbol? type = Comp0[Main].GetTypeByMetadataName(typeName);
        if (type == null)
        {
            _report.Error("spec-member-missing", $"{what}: type {typeName} not found");
            return null;
        }
        var candidates = type.GetMembers(member).Where(m => m is IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol && !m.IsImplicitlyDeclared).ToList();
        if (sig.Length > 0)
        {
            // Signature as written in specs: "(Verse.Pawn,System.Int32)", "(ref int,int,out int)"; nullable annotations ignored.
            static string Norm(string s) => s.Replace(" ", "").Replace("?", "").Replace("System.Int32", "int").Replace("System.Single", "float").Replace("System.Boolean", "bool").Replace("System.String", "string").Replace("ref", "ref ").Replace("out", "out ");
            candidates = candidates.Where(m => m is IMethodSymbol ms && Norm("(" + string.Join(",", ms.Parameters.Select(p => (p.RefKind == RefKind.None ? "" : p.RefKind == RefKind.Out ? "out " : "ref ") + p.Type.ToDisplayString())) + ")") == Norm(sig)).ToList();
        }
        if (candidates.Count == 0)
        {
            _report.Error("spec-member-missing", $"{what}: member {spec} not found");
            return null;
        }
        if (candidates.Count > 1)
        {
            _report.Error("spec-member-ambiguous", $"{what}: {spec} matches {candidates.Count} overloads; add the parameter list, e.g. {typeName}.{member}({string.Join(",", ((IMethodSymbol)candidates[0]).Parameters.Select(p => p.Type.ToDisplayString()))})");
            return null;
        }
        return candidates[0];
    }

    private static readonly HashSet<string> HarmonyConventionNames = new(StringComparer.Ordinal)
    {
        "Prepare", "TargetMethod", "TargetMethods", "Prefix", "Postfix", "Transpiler", "Finalizer", "Cleanup",
    };

    private void BuildMemberRenames()
    {
        foreach (MemberRenameEntry e in _m.Spec.MemberRenames)
        {
            ISymbol? sym = ResolveMember(e.Member, "memberRenames");
            if (sym == null)
            {
                continue;
            }
            if (sym.IsOverride || sym is IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 } || sym is IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 } || sym is IMethodSymbol { MethodKind: not MethodKind.Ordinary })
            {
                _report.Error("member-rename-unsupported", $"{e.Member}: overrides, interface implementations and special methods are not renamed");
                continue;
            }
            if (HarmonyConventionNames.Contains(sym.Name) && sym.ContainingType.GetAttributes().Any(a => a.AttributeClass?.Name == "HarmonyPatch"))
            {
                _report.Error("member-rename-harmony", $"{e.Member}: Harmony binds this method by name");
                continue;
            }
            if (sym.GetAttributes().Any(a => a.AttributeClass?.Name is "TweakValueAttribute" or "DebugActionAttribute"))
            {
                _report.Error("member-rename-devtool", $"{e.Member}: [TweakValue]/[DebugAction] members keep their names (saved in prefs / shown in menus)");
                continue;
            }
            if (sym.ContainingType.GetMembers(e.New).Any(m => !_m.Spec.MemberRenames.Any(o => o.Member.StartsWith(sym.ContainingType.ToDisplayString() + "." + m.Name, StringComparison.Ordinal))))
            {
                _report.Error("member-rename-clash", $"{e.Member} -> {e.New}: {sym.ContainingType.ToDisplayString()} already has a member named {e.New}");
                continue;
            }
            string oldId = sym.OriginalDefinition.GetDocumentationCommentId()!;
            _defOfFieldRenames[oldId] = e.New;
            if (sym is IFieldSymbol { IsStatic: false } xf)
            {
                XmlFieldRenames.Add((MetadataKey(xf.ContainingType), xf.Name, e.New));
            }
            if (e.AddAttributes is { Count: > 0 })
            {
                _memberAddAttributes[oldId] = e.AddAttributes;
            }
            string mappedOld = _m.MapDocString(oldId);
            int paren = mappedOld.IndexOf('(');
            string head = paren < 0 ? mappedOld : mappedOld.Substring(0, paren);
            string tail = paren < 0 ? "" : mappedOld.Substring(paren);
            int lastDot = head.LastIndexOf('.');
            _defOfMappedKeys[mappedOld] = head.Substring(0, lastDot + 1) + e.New + tail;
            _report.Maps.Members.Add(new MemberMapEntry
            {
                Old = oldId.Substring(2), New = _defOfMappedKeys[mappedOld].Substring(2), Kind = "rename", Module = e.Module,
                Attributes = e.AddAttributes,
            });
        }
    }

    private bool PendingRenameAway(INamedTypeSymbol t, string memberName) =>
        t.GetMembers(memberName).All(m => m.GetDocumentationCommentId() is string id && _defOfFieldRenames.ContainsKey(id));

    // [DefAlias("x")] on a [DefOf] field binds def "x" of the field's root type; a renamed x renames the attribute argument.
    private static string? DefAliasOf(IFieldSymbol f) =>
        f.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "DefAliasAttribute")?.ConstructorArguments.FirstOrDefault().Value as string;

    private void RecordDefAliasLiteral(IFieldSymbol f, DefRename dr)
    {
        AttributeData? a = f.GetAttributes().FirstOrDefault(x => x.AttributeClass?.Name == "DefAliasAttribute");
        if (a?.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax syntax || syntax.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not LiteralExpressionSyntax lit)
        {
            _report.Error("defalias-unresolved", $"[DefAlias] on {f.ContainingType.ToDisplayString()}.{f.Name}: argument is not a literal");
            return;
        }
        _aliasLiteralRenames[(syntax.SyntaxTree.FilePath, lit.Token.SpanStart)] = dr.New;
        dr.UsedExplicitRefs.Add(_repo.Rel(syntax.SyntaxTree.FilePath) + ":" + (lit.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
    }

    // Attribute lines inserted above a renamed member's declaration.
    private void AddMemberAttributes(DocPlan plan, MemberDeclarationSyntax decl, List<string> attributes)
    {
        string text = plan.Doc.Text0;
        int lineStart = text.LastIndexOf('\n', Math.Max(0, decl.SpanStart - 1)) + 1;
        string indent = text.Substring(lineStart, decl.SpanStart - lineStart);
        if (indent.Trim().Length > 0)
        {
            _report.Error("member-attribute-position", "declaration does not start its line; cannot add attributes", RelOf(plan.Doc), LinesOf(plan.Doc).Get(decl.SpanStart).Line);
            return;
        }
        var sb = new StringBuilder();
        foreach (string a in attributes)
        {
            sb.Append(indent).Append('[').Append(a).Append(']').Append(plan.Doc.NewLine);
        }
        plan.TriviaEdits.Add(new TextEdit(lineStart, 0, sb.ToString(), "member-attribute"));
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Created files (verbatim content, compiled only in the final state)
    // ------------------------------------------------------------------------------------------------------------------

    private void AddCreatedFiles()
    {
        foreach (CreateFileEntry e in _m.Spec.CreateFiles)
        {
            string full = _repo.Full(RepoPaths.Norm(e.Path));
            if (File.Exists(full))
            {
                _report.Error("create-file-exists", $"createFiles: {e.Path} already exists");
                continue;
            }
            ProjectModel? home = Projects.Where(p => full.StartsWith(p.Directory, StringComparison.OrdinalIgnoreCase)).OrderByDescending(p => p.Directory.Length).FirstOrDefault();
            if (home == null)
            {
                _report.Error("create-file-project", $"createFiles: {e.Path} is not inside a project directory");
                continue;
            }
            string nl = home == Main ? Docs.First(d => d.Projects.Contains(Main) && !d.Created).NewLine : "\r\n";
            string content = e.Content.Replace("\r\n", "\n").Replace("\n", nl);
            var doc = new CsDoc
            {
                Key = "create:" + full, OrigFull = null, OutFull = full, Text0 = content, Bom = false, NewLine = nl,
                Created = true, Editable = true, FinalOnly = true,
            };
            doc.Projects.Add(home);
            Docs.Add(doc);
            _docByKey[doc.Key] = doc;
            _report.FileOps.Add(new FileOp { Op = "create", From = "(spec)", To = _repo.Rel(full), Note = e.Reason });
            _report.Maps.CreatedFiles.Add(new NameMapEntry { Old = "", New = _repo.Rel(full), Module = e.Module });
        }
    }
}
