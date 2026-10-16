using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RenameApply;

public sealed class EditRecord
{
    public string File { get; set; } = "";        // repo-relative path BEFORE the run
    public string? NewFile { get; set; }          // repo-relative path AFTER the run, when the file moved
    public int Line { get; set; }                 // 1-based, in the file as it was before this edit pass
    public int Column { get; set; }
    public string Kind { get; set; } = "";
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public string? BeforeLine { get; set; }
    public string? AfterLine { get; set; }
    public string? Note { get; set; }
}

public sealed class Finding
{
    public string Severity { get; set; } = "warning"; // error | warning | info
    public string Kind { get; set; } = "";
    public string? File { get; set; }
    public int? Line { get; set; }
    public string? Text { get; set; }
    public string Message { get; set; } = "";
}

public sealed class FileOp
{
    public string Op { get; set; } = "";   // move | create | delete | rmdir
    public string From { get; set; } = "";
    public string? To { get; set; }
    public string? Note { get; set; }
}

public sealed class ProjectCompileResult
{
    public string Project { get; set; } = "";
    public int BaselineErrors { get; set; }
    public int FinalErrors { get; set; }
    public List<string> NewErrors { get; set; } = new();
    public List<string> BaselineErrorSamples { get; set; } = new();
}

public sealed class TypeMapEntry
{
    public string Old { get; set; } = "";
    public string New { get; set; } = "";
    public bool Implicit { get; set; }            // nested type that moved with its outer type
    public bool? SaveVisible { get; set; }
    public bool? XmlVisible { get; set; }
    public string? Module { get; set; }
    public System.Text.Json.JsonElement? Transitional { get; set; }
    public string? PublicAlias { get; set; }
}

public sealed class DefMapEntry
{
    public string DefType { get; set; } = "";
    public string? RootDefType { get; set; }
    public string Old { get; set; } = "";
    public string New { get; set; } = "";
    public bool Derived { get; set; }
    public string? DerivedBy { get; set; }         // shape | referenced | unknown shape (derived rows only)
    public bool? SaveVisible { get; set; }
    public bool? NewNameSharedWithOtherDefTypes { get; set; }
    public string? Module { get; set; }
}

public sealed class MemberMapEntry
{
    public string Old { get; set; } = "";          // doc-id body of the member before (e.g. Mugirl.Mugirl_DefOf.Job_Nuzzle)
    public string New { get; set; } = "";          // doc-id body after
    public string Kind { get; set; } = "";         // rename | move | copy | delete
    public List<string>? Attributes { get; set; }
    public string? Module { get; set; }
}

public sealed class SplitMapEntry
{
    public string Kind { get; set; } = "";         // def | operation | keyed | definjected
    public string Name { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string? Language { get; set; }
}

public sealed class NameMapEntry
{
    public string Old { get; set; } = "";
    public string New { get; set; } = "";
    public string? Module { get; set; }
}

public sealed class Maps
{
    public List<TypeMapEntry> Types { get; set; } = new();
    public List<DefMapEntry> Defs { get; set; } = new();
    public List<NameMapEntry> Keyed { get; set; } = new();
    public List<NameMapEntry> AbstractNames { get; set; } = new();
    public List<NameMapEntry> DefOfFields { get; set; } = new();
    public List<NameMapEntry> Files { get; set; } = new();
    public List<MemberMapEntry> Members { get; set; } = new();
    public List<NameMapEntry> CreatedFiles { get; set; } = new();
    public List<SplitMapEntry> Splits { get; set; } = new();
    public List<NameMapEntry> AttributeArguments { get; set; } = new();
}

public sealed class RunReport
{
    public string Tool { get; set; } = "RenameApply";
    public int FormatVersion { get; set; } = 1;
    public bool DryRun { get; set; }
    public bool Written { get; set; }
    public string Repo { get; set; } = "";
    public List<string> Specs { get; set; } = new();
    public DateTimeOffset? StartedUtc { get; set; }
    public Dictionary<string, int> Summary { get; set; } = new();
    public Maps Maps { get; set; } = new();
    public List<FileOp> FileOps { get; set; } = new();
    public List<EditRecord> Edits { get; set; } = new();
    public List<Finding> Findings { get; set; } = new();
    public List<ProjectCompileResult> Compile { get; set; } = new();
    public List<string> Notes { get; set; } = new();

    [JsonIgnore] public int ErrorCount => Findings.Count(f => f.Severity == "error");

    public void Error(string kind, string message, string? file = null, int? line = null, string? text = null) =>
        Findings.Add(new Finding { Severity = "error", Kind = kind, Message = message, File = file, Line = line, Text = text });

    public void Warn(string kind, string message, string? file = null, int? line = null, string? text = null) =>
        Findings.Add(new Finding { Severity = "warning", Kind = kind, Message = message, File = file, Line = line, Text = text });

    public void Info(string kind, string message, string? file = null, int? line = null, string? text = null) =>
        Findings.Add(new Finding { Severity = "info", Kind = kind, Message = message, File = file, Line = line, Text = text });

    public void Finish()
    {
        Edits = Edits.OrderBy(e => e.File, StringComparer.Ordinal).ThenBy(e => e.Line).ThenBy(e => e.Column).ThenBy(e => e.Kind, StringComparer.Ordinal).ToList();
        Findings = Findings
            .DistinctBy(f => (f.Severity, f.Kind, f.File, f.Line, f.Message, f.Text))
            .OrderBy(f => f.Severity switch { "error" => 0, "warning" => 1, _ => 2 })
            .ThenBy(f => f.Kind, StringComparer.Ordinal).ThenBy(f => f.File ?? "", StringComparer.Ordinal).ThenBy(f => f.Line ?? 0)
            .ToList();
        Summary.Clear();
        foreach (var g in Edits.GroupBy(e => "edit:" + e.Kind))
        {
            Summary[g.Key] = g.Count();
        }
        foreach (var g in FileOps.GroupBy(o => "fileop:" + o.Op))
        {
            Summary[g.Key] = g.Count();
        }
        foreach (var g in Findings.GroupBy(f => $"{f.Severity}:{f.Kind}"))
        {
            Summary[g.Key] = g.Count();
        }
        Summary["files-edited"] = Edits.Select(e => e.File).Distinct().Count();
        Summary = Summary.OrderBy(k => k.Key, StringComparer.Ordinal).ToDictionary(k => k.Key, k => k.Value);
    }

    public void Save(string path)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, options) + "\n", new System.Text.UTF8Encoding(false));
    }

    public List<string> Waves { get; set; } = new();

    // rename-applied log: what the run actually changed, keyed by old names. Generators of the upgrader tables (K1 manifest
    // types/defs) read this, never the spec, so code and map cannot diverge.
    public void SaveApplied(string path)
    {
        var applied = new
        {
            tool = Tool, formatVersion = 1, kind = "rename-applied", waves = Waves, dryRun = DryRun, written = Written, repo = Repo, specs = Specs,
            startedUtc = StartedUtc, errors = ErrorCount,
            types = Maps.Types, defs = Maps.Defs, defOfFields = Maps.DefOfFields, members = Maps.Members, keyed = Maps.Keyed, abstractNames = Maps.AbstractNames,
            files = Maps.Files, createdFiles = Maps.CreatedFiles, splits = Maps.Splits, attributeArguments = Maps.AttributeArguments,
            fileOps = FileOps,
        };
        var options = new JsonSerializerOptions
        {
            WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(applied, options) + "\n", new System.Text.UTF8Encoding(false));
    }

    public void Print(TextWriter w, bool full)
    {
        w.WriteLine($"== RenameApply {(DryRun ? "DRY RUN" : Written ? "APPLIED" : "NOT WRITTEN")} — repo {Repo}");
        foreach (FileOp op in FileOps)
        {
            w.WriteLine($"  {op.Op,-7} {op.From}{(op.To != null ? " -> " + op.To : "")}{(op.Note != null ? "  (" + op.Note + ")" : "")}");
        }
        if (full)
        {
            foreach (var g in Edits.GroupBy(e => e.File))
            {
                EditRecord first = g.First();
                w.WriteLine($"-- {g.Key}{(first.NewFile != null && first.NewFile != g.Key ? " => " + first.NewFile : "")}");
                foreach (EditRecord e in g)
                {
                    w.WriteLine($"   {e.Line,5}:{e.Column,-4} {e.Kind,-24} {Clip(e.Before)} -> {Clip(e.After)}{(e.Note != null ? "   [" + e.Note + "]" : "")}");
                }
            }
        }
        foreach (Finding f in Findings.Where(f => full || f.Severity != "info"))
        {
            w.WriteLine($"  {f.Severity.ToUpperInvariant(),-7} {f.Kind}: {(f.File != null ? f.File + (f.Line != null ? ":" + f.Line : "") + " " : "")}{f.Message}{(f.Text != null ? "  «" + Clip(f.Text) + "»" : "")}");
        }
        foreach (ProjectCompileResult c in Compile)
        {
            w.WriteLine($"  compile {c.Project}: baseline errors {c.BaselineErrors}, final errors {c.FinalErrors}, new {c.NewErrors.Count}");
            foreach (string e in c.NewErrors.Take(40))
            {
                w.WriteLine("     " + e);
            }
        }
        w.WriteLine("== summary");
        foreach (var kv in Summary)
        {
            w.WriteLine($"  {kv.Value,6}  {kv.Key}");
        }
    }

    private static string Clip(string s)
    {
        s = s.Replace("\r", "\\r").Replace("\n", "\\n");
        return s.Length > 160 ? s.Substring(0, 157) + "..." : s;
    }
}
