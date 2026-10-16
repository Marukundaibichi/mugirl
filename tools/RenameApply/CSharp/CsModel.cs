using Microsoft.CodeAnalysis;

namespace RenameApply.CSharp;

// One physical C# file (or one created by extracting a type). Shared by every project that compiles it (linked files).
public sealed class CsDoc
{
    public required string Key { get; init; }       // original full path; "new:<full path>" for created files
    public string? OrigFull { get; init; }           // null for created files
    public required string OutFull { get; set; }     // full path after the run
    public required string Text0 { get; set; }       // text before rename edits (after type extraction)
    public required bool Bom { get; init; }
    public required string NewLine { get; init; }
    public bool Created { get; init; }
    public bool FinalOnly { get; init; }             // created from spec content: compiled only in the final state
    public bool Editable { get; init; }
    public bool Extracted { get; set; }              // types were extracted from this file in phase 0
    public string? ExtractedFrom { get; init; }      // rel path of the source of a created file
    // 拆分后的文件仍占据原编译输入的位置；同一输入内按原顶层声明的位置排列。
    public string? OrderSource { get; init; }
    public int OrderPosition { get; set; }
    public bool Emptied { get; set; }                // every type left the file: it is deleted
    public List<(int NewLine, int OrigLine, int Count)> LineMap { get; } = new();   // extracted file line -> source line
    public List<ProjectModel> Projects { get; } = new();
    public string? Text1 { get; set; }               // final text

    public string ViewText => Text1 ?? Text0;
    public override string ToString() => Key;
}

public enum SiteKind
{
    Name,           // any simple name (binding check only unless edited)
    TypeRef,        // simple name bound to a type
    QualifiedType,  // namespace-qualified reference to a renamed type: Left.Name replaced as a whole
    Declaration,    // identifier of a type, constructor or destructor declaration
    DefOfField,     // [DefOf] field declaration or reference
    NamespaceLeft,  // leftmost identifier bound to a namespace
}

// A simple-name occurrence: the unit of renaming, qualification fallback and binding verification.
public sealed class Site
{
    public required CsDoc Doc { get; init; }
    public required int Start { get; init; }          // identifier token start in Text0
    public required string OrigText { get; init; }    // identifier token text
    public SiteKind Kind { get; set; } = SiteKind.Name;
    public int ReplaceStart { get; set; }             // replaced span in Text0 (identifier, or Left..identifier)
    public int ReplaceLength { get; set; }
    public string NewText { get; set; } = "";         // planned text for the replaced span
    public int AnchorOffset { get; set; }             // offset of the final identifier inside NewText
    public string? QualifiedText { get; set; }        // "Ns.Outer.Name" fallback for type references (no global::)
    public bool QualifyNeedsGlobal { get; set; }
    public bool Qualify { get; set; }                 // set by the fix loop
    public string? ExpectedKey { get; set; }          // symbol key expected after the rewrite (null: not checked)
    // 链接文件在每个项目中的绑定不同；其他项目解析出的符号不能替代本项目原本未解析的名字。
    public HashSet<ProjectModel> UnresolvedBaselineProjects { get; } = new();
    public string? EditKind { get; set; }             // report kind when edited
    public string? BoundNamespace { get; set; }       // NamespaceLeft: the namespace the identifier denotes
    public bool Removed { get; set; }                 // covered by a qualified replacement

    public string EffectiveText
    {
        get
        {
            if (Qualify && QualifiedText != null)
            {
                return (QualifyNeedsGlobal ? "global::" : "") + QualifiedText;
            }
            return NewText;
        }
    }

    public int EffectiveAnchor
    {
        get
        {
            if (Qualify && QualifiedText != null)
            {
                string t = EffectiveText;
                int dot = t.LastIndexOf('.');
                return dot + 1;
            }
            return AnchorOffset;
        }
    }

    public bool IsEdit => EffectiveText != Doc.Text0.Substring(ReplaceStart, ReplaceLength);
}

public enum RequirementKind
{
    Type,
    Extension,
}

public sealed class Requirement
{
    public required ProjectModel Project { get; init; }
    public required Site Site { get; init; }
    public required string Namespace { get; init; }   // namespace that must be in scope after the rewrite
    public required RequirementKind Kind { get; init; }
}

public sealed class UsingInfo
{
    public required int LineStart { get; init; }      // span of the whole line(s) holding the directive, in Text0
    public required int LineEnd { get; init; }
    public required int Start { get; init; }          // directive span
    public required string NamespaceName { get; init; } // as written
    public string? ResolvedNamespace { get; set; }
    public bool InsideNamespace { get; init; }
}

public sealed class DocPlan
{
    public required CsDoc Doc { get; init; }
    public Dictionary<int, Site> Sites { get; } = new();
    public List<Requirement> Requirements { get; } = new();
    public List<TextEdit> TriviaEdits { get; } = new();            // literals and comments
    public List<(int Start, int Length, string NewName, string OldName)> NamespaceDecls { get; } = new();
    public List<UsingInfo> Usings { get; } = new();
    public SortedSet<string> AddUsings { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> ExtraNamespaces { get; } = new(StringComparer.Ordinal);  // must be in scope (attribute rewrites)
    public HashSet<int> RemoveUsings { get; } = new();             // UsingInfo.Start of directives to delete
    public List<TextEdit>? AppliedEdits { get; set; }
}
