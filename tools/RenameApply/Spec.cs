using System.Text.Json;
using System.Text.Json.Serialization;

namespace RenameApply;

// Wire format of one module spec (design.md section 4 / task OUTPUT 1). Unknown properties are ignored.
public sealed class SpecFile
{
    public string? Module { get; set; }
    public string? Wave { get; set; }
    public List<string>? OwnedSourceFolders { get; set; }
    public List<string>? OwnedContentFiles { get; set; }
    public List<TypeEntry>? Types { get; set; }
    public List<MoveEntry>? FileMoves { get; set; }
    public List<DefEntry>? Defs { get; set; }
    public List<RenameEntry>? AbstractNames { get; set; }
    public List<RenameEntry>? Keyed { get; set; }
    public List<MoveEntry>? ContentFileMoves { get; set; }
    public List<string>? Notes { get; set; }

    // Extensions (R and R2): see USAGE.md "Spec format".
    public List<MemberRenameEntry>? MemberRenames { get; set; }
    public List<CreateFileEntry>? CreateFiles { get; set; }
    public List<CreateTypeEntry>? CreateTypes { get; set; }
    public List<MemberMoveEntry>? MemberMoves { get; set; }
    public List<MemberDeleteEntry>? MemberDeletes { get; set; }
    public List<DissolveTypeEntry>? DissolveTypes { get; set; }
    public List<AttributeArgumentEntry>? AttributeArgumentRewrites { get; set; }
    public List<TextEditEntry>? TextEdits { get; set; }
    public List<XmlSplitEntry>? XmlSplits { get; set; }
    public List<KeyedSplitEntry>? KeyedSplits { get; set; }
    public List<BaselineAppendEntry>? BaselineAppends { get; set; }
    public List<BaselineRemovalEntry>? BaselineRemovals { get; set; }
}

// Lines appended to a validator baseline (entries the renamed tree needs, e.g. allow-listing the public alias types).
public sealed class BaselineAppendEntry
{
    public string File { get; set; } = "";
    public List<string> Lines { get; set; } = new();
    public string? Reason { get; set; }
    [JsonIgnore] public string Module { get; set; } = "";
}

// Lines removed from a validator baseline, matched by their key (text before the first TAB) BEFORE any rename of the run:
// allow-list debt that the run pays off (e.g. "root: Mugirl.X" once X leaves the root namespace).
public sealed class BaselineRemovalEntry
{
    public string File { get; set; } = "";
    public List<string> Keys { get; set; } = new();
    public string? Reason { get; set; }
    [JsonIgnore] public string Module { get; set; } = "";
}

// Rename of a field/property/method symbol (all references); optional attributes added to its declaration.
public sealed class MemberRenameEntry
{
    public string Member { get; set; } = "";          // "Ns.Type.Member" (metadata type name)
    public string New { get; set; } = "";
    public List<string>? AddAttributes { get; set; }  // e.g. DefAlias("Mugirl_Nuzzle")
    public string? Reason { get; set; }
    [JsonIgnore] public string Module { get; set; } = "";
}

// A file written verbatim (new code the rename needs, e.g. public alias types). It must not exist yet.
public sealed class CreateFileEntry
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Reason { get; set; }
    [JsonIgnore] public string Module { get; set; } = "";
}

// A new type created from a template (R2): kind "defOf" | "staticClass" | "enum".
public sealed class CreateTypeEntry
{
    public string FullName { get; set; } = "";
    public string File { get; set; } = "";
    public string Kind { get; set; } = "staticClass";
    public string? Accessibility { get; set; }       // default internal
    public string? Header { get; set; }              // comment above the type (one line per \n)
    public List<string>? Members { get; set; }       // enum members
    public List<string>? LinkWith { get; set; }      // files other projects link by path: those projects link the new file too
    public string? Reason { get; set; }
    [JsonIgnore] public string Module { get; set; } = "";
}

// Static members moved to another class with every reference (R2). When referenceRules are given, the declaration is
// copied into every target named by a rule and each reference is rewritten to the target of the first rule whose path
// prefix matches the referencing file's FINAL path ("to" otherwise).
public sealed class MemberMoveEntry
{
    public string To { get; set; } = "";
    public List<string> Members { get; set; } = new();
    public List<ReferenceRule>? ReferenceRules { get; set; }
    public string? Accessibility { get; set; }        // e.g. "internal"/"private": rewrites the declaration's modifier
    public string? Order { get; set; }                // "source" (default: declaration order) | "spec" (order of Members, e.g. overloads together)
    public string? Reason { get; set; }
    [JsonIgnore] public string Module { get; set; } = "";
}

public sealed class ReferenceRule
{
    public string PathPrefix { get; set; } = "";
    public string To { get; set; } = "";
}

public sealed class MemberDeleteEntry
{
    public List<string> Members { get; set; } = new();
    public string? Reason { get; set; }
    [JsonIgnore] public string Module { get; set; } = "";
}

// A type that must be empty after the moves (only a DefOf static ctor may remain): deleted, with its file when empty.
public sealed class DissolveTypeEntry
{
    public string Type { get; set; } = "";
    public string? Reason { get; set; }
    [JsonIgnore] public string Module { get; set; } = "";
}

// Rewrites one positional argument of every usage of an attribute. The new expression is computed from the FINAL file path
// of the attributed declaration (first matching rule) and formatted with valueFormat ("{0}" = rule value).
public sealed class AttributeArgumentEntry
{
    public string Attribute { get; set; } = "";       // full metadata name of the attribute class
    public int Argument { get; set; }
    public string ValueFormat { get; set; } = "{0}";
    public List<PathValueRule> Rules { get; set; } = new();
    public string? RequireNamespace { get; set; }     // namespace that must be in scope for the new expression
    public string? Reason { get; set; }
    [JsonIgnore] public string Module { get; set; } = "";
}

public sealed class PathValueRule
{
    public string PathPrefix { get; set; } = "";      // repo-relative, final layout
    public string Value { get; set; } = "";           // "{folder}" = the path segment right after PathPrefix
}

// Exact, unique text replacement in one file (e.g. a signature change no symbol rename expresses). Verified by compiling.
public sealed class TextEditEntry
{
    public string File { get; set; } = "";
    public string Old { get; set; } = "";
    public string New { get; set; } = "";
    public string? Reason { get; set; }
    [JsonIgnore] public string Module { get; set; } = "";
}

// Moves top-level Def elements (or Patch operations) from one XML file into others (created or existing), keeping their
// order, leading comments and blank lines; DefInjected entries of moved defs follow into the DefInjected file named after
// the target, in every language. The source is deleted when nothing but its root element remains.
public sealed class XmlSplitEntry
{
    public string Source { get; set; } = "";
    public List<XmlSplitTarget> Targets { get; set; } = new();
    public string? Reason { get; set; }
    [JsonIgnore] public string Module { get; set; } = "";
}

public sealed class XmlSplitTarget
{
    public string File { get; set; } = "";
    public List<string>? Defs { get; set; }           // "defName", "DefType/defName" or "Name=<abstract name>"
    public List<int>? Operations { get; set; }        // Patch files: 1-based indices of the top-level elements
    public List<string>? OperationFingerprints { get; set; } // a substring each listed operation must contain
    public string? Owner { get; set; }
    public string? Header { get; set; }               // created target: text placed right after the root start tag (e.g. PatchGovernance)
}

// Keyed file split: each key moves to the named file in the same Keyed folder, in every language; order and comments kept.
public sealed class KeyedSplitEntry
{
    public string Source { get; set; } = "";          // file name under <root>/Languages/<lang>/Keyed/
    public string? ContentRoot { get; set; }          // default "1.7"
    public Dictionary<string, string> Keys { get; set; } = new();   // key -> target file name
    public List<string>? DeleteKeys { get; set; }
    public string? Reason { get; set; }
    [JsonIgnore] public string Module { get; set; } = "";
}

public sealed class TypeEntry
{
    public string Old { get; set; } = "";
    public string New { get; set; } = "";
    public string? OldFile { get; set; }
    public string? NewFile { get; set; }
    public bool? SaveVisible { get; set; }
    public bool? XmlVisible { get; set; }
    public string? Reason { get; set; }
    public JsonElement? Transitional { get; set; }     // C23 marker {"waveS", "upgrade"} (false/absent: not transitional)
    public JsonElement? PublicAlias { get; set; }      // D4: the old name stays as a public alias subclass
    [JsonIgnore] public string Module { get; set; } = "";
}

public sealed class MoveEntry
{
    public string Old { get; set; } = "";
    public string New { get; set; } = "";
    [JsonIgnore] public string Module { get; set; } = "";
}

public sealed class DefEntry
{
    public string DefType { get; set; } = "";
    public string Old { get; set; } = "";
    public string New { get; set; } = "";
    public bool? SaveVisible { get; set; }
    public bool SharedNameWithOtherDefTypes { get; set; }
    public List<string>? ExplicitRefs { get; set; }
    public List<string>? NotRefs { get; set; }        // "path:LINE" sites that carry the old text but do NOT mean this def
    public bool? NewNameSharedWithOtherDefTypes { get; set; }
    public string? Reason { get; set; }
    [JsonIgnore] public string Module { get; set; } = "";
}

public sealed class RenameEntry
{
    public string Old { get; set; } = "";
    public string New { get; set; } = "";
    public string? Reason { get; set; }
    [JsonIgnore] public string Module { get; set; } = "";
}

// Union of every module spec, de-duplicated. Conflicts are reported by SpecValidator.
public sealed class MergedSpec
{
    public List<string> Sources { get; } = new();
    public SortedSet<string> Waves { get; } = new(StringComparer.Ordinal);
    public List<TypeEntry> Types { get; } = new();
    public List<MoveEntry> FileMoves { get; } = new();
    public List<DefEntry> Defs { get; } = new();
    public List<RenameEntry> AbstractNames { get; } = new();
    public List<RenameEntry> Keyed { get; } = new();
    public List<MoveEntry> ContentFileMoves { get; } = new();
    public List<string> Notes { get; } = new();
    public List<MemberRenameEntry> MemberRenames { get; } = new();
    public List<CreateFileEntry> CreateFiles { get; } = new();
    public List<CreateTypeEntry> CreateTypes { get; } = new();
    public List<MemberMoveEntry> MemberMoves { get; } = new();
    public List<MemberDeleteEntry> MemberDeletes { get; } = new();
    public List<DissolveTypeEntry> DissolveTypes { get; } = new();
    public List<AttributeArgumentEntry> AttributeArgumentRewrites { get; } = new();
    public List<TextEditEntry> TextEdits { get; } = new();
    public List<XmlSplitEntry> XmlSplits { get; } = new();
    public List<KeyedSplitEntry> KeyedSplits { get; } = new();
    public List<BaselineAppendEntry> BaselineAppends { get; } = new();
    public List<BaselineRemovalEntry> BaselineRemovals { get; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // Accepts: a file holding one spec object, an array of spec objects, or {"specs": [...]}; or a directory of *.json specs.
    public static MergedSpec Load(IEnumerable<string> inputs)
    {
        var merged = new MergedSpec();
        foreach (string input in inputs)
        {
            IEnumerable<string> files = Directory.Exists(input)
                ? Directory.EnumerateFiles(input, "*.json", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal)
                : new[] { input };
            foreach (string file in files)
            {
                string json = File.ReadAllText(file);
                using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var specs = new List<SpecFile>();
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    specs.AddRange(doc.RootElement.Deserialize<List<SpecFile>>(JsonOptions) ?? new());
                }
                else if (doc.RootElement.TryGetProperty("specs", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    specs.AddRange(arr.Deserialize<List<SpecFile>>(JsonOptions) ?? new());
                }
                else
                {
                    specs.Add(doc.RootElement.Deserialize<SpecFile>(JsonOptions) ?? new SpecFile());
                }
                foreach (SpecFile s in specs)
                {
                    merged.Add(s, file);
                }
                merged.Sources.Add(Path.GetFullPath(file));
            }
        }
        return merged;
    }

    private void Add(SpecFile s, string file)
    {
        string module = s.Module ?? Path.GetFileNameWithoutExtension(file);
        if (s.Wave != null)
        {
            Waves.Add(s.Wave);
        }
        foreach (TypeEntry t in s.Types ?? new()) { t.Module = module; Types.Add(t); }
        foreach (MoveEntry m in s.FileMoves ?? new()) { m.Module = module; FileMoves.Add(m); }
        foreach (DefEntry d in s.Defs ?? new()) { d.Module = module; Defs.Add(d); }
        foreach (RenameEntry a in s.AbstractNames ?? new()) { a.Module = module; AbstractNames.Add(a); }
        foreach (RenameEntry k in s.Keyed ?? new()) { k.Module = module; Keyed.Add(k); }
        foreach (MoveEntry m in s.ContentFileMoves ?? new()) { m.Module = module; ContentFileMoves.Add(m); }
        foreach (string n in s.Notes ?? new()) { Notes.Add($"[{module}] {n}"); }
        foreach (MemberRenameEntry x in s.MemberRenames ?? new()) { x.Module = module; MemberRenames.Add(x); }
        foreach (CreateFileEntry x in s.CreateFiles ?? new()) { x.Module = module; CreateFiles.Add(x); }
        foreach (CreateTypeEntry x in s.CreateTypes ?? new()) { x.Module = module; CreateTypes.Add(x); }
        foreach (MemberMoveEntry x in s.MemberMoves ?? new()) { x.Module = module; MemberMoves.Add(x); }
        foreach (MemberDeleteEntry x in s.MemberDeletes ?? new()) { x.Module = module; MemberDeletes.Add(x); }
        foreach (DissolveTypeEntry x in s.DissolveTypes ?? new()) { x.Module = module; DissolveTypes.Add(x); }
        foreach (AttributeArgumentEntry x in s.AttributeArgumentRewrites ?? new()) { x.Module = module; AttributeArgumentRewrites.Add(x); }
        foreach (TextEditEntry x in s.TextEdits ?? new()) { x.Module = module; TextEdits.Add(x); }
        foreach (XmlSplitEntry x in s.XmlSplits ?? new()) { x.Module = module; XmlSplits.Add(x); }
        foreach (KeyedSplitEntry x in s.KeyedSplits ?? new()) { x.Module = module; KeyedSplits.Add(x); }
        foreach (BaselineAppendEntry x in s.BaselineAppends ?? new()) { x.Module = module; BaselineAppends.Add(x); }
        foreach (BaselineRemovalEntry x in s.BaselineRemovals ?? new()) { x.Module = module; BaselineRemovals.Add(x); }
    }
}
