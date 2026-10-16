using System.Text.Json;
using RenameApply.Xml;

namespace RenameApply;

// Writes the pending changes as one unit. Before the first write every file the run overwrites or deletes is copied into a
// backup folder together with backup-manifest.json (files to restore, files the run creates, directories it removes). Any
// exception while writing puts the tree back from that folder; an interrupted process leaves the folder for --restore.
public static class Transaction
{
    private sealed class Manifest
    {
        public string Repo { get; set; } = "";
        public List<string> Restore { get; set; } = new();     // repo-relative files copied into the backup
        public List<string> Created { get; set; } = new();     // repo-relative files that did not exist before the run
        public bool Completed { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void Apply(RepoPaths repo, List<PendingWrite> writes, RunReport report, string? backupDir, bool keepBackup, TextWriter log)
    {
        backupDir = Path.GetFullPath(backupDir ?? Path.Combine(Path.GetTempPath(), "RenameApply", "backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        if (repo.IsInside(backupDir))
        {
            throw new ToolException($"--backup {backupDir} must be outside the tree being renamed");
        }
        if (Directory.Exists(backupDir) && Directory.EnumerateFileSystemEntries(backupDir).Any())
        {
            throw new ToolException($"backup folder {backupDir} is not empty");
        }
        var manifest = new Manifest { Repo = repo.Root };
        var touched = writes.SelectMany(w => new[] { w.OutFull, w.DeleteFull }).Where(p => p != null).Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (string full in touched)
        {
            if (File.Exists(full))
            {
                string rel = OnDiskRel(repo, full);
                string dest = Path.Combine(backupDir, "files", rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(full, dest);
                manifest.Restore.Add(rel);
            }
            else
            {
                manifest.Created.Add(repo.Rel(full));
            }
        }
        // A case-only rename creates the new spelling of a file that exists under the old one.
        foreach (PendingWrite w in writes.Where(w => w.Text != null && w.DeleteFull != null && string.Equals(w.DeleteFull, w.OutFull, StringComparison.OrdinalIgnoreCase)
                                                     && !string.Equals(w.DeleteFull, w.OutFull, StringComparison.Ordinal)))
        {
            manifest.Created.Add(repo.Rel(w.OutFull));
        }
        string manifestPath = Path.Combine(backupDir, "backup-manifest.json");
        Directory.CreateDirectory(backupDir);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, Json));
        log.WriteLine($"backup: {manifest.Restore.Count} files saved, {manifest.Created.Count} to be created; {backupDir}");
        try
        {
            ApplyWrites(repo, writes, report);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report.Error("apply-failed", $"writing failed ({ex.Message}); restoring the tree from {backupDir}");
            Restore(backupDir, log);
            report.Error("apply-rolled-back", $"the tree was restored from {backupDir}; nothing of this run remains");
            throw new ToolException($"apply failed and was rolled back: {ex.Message}");
        }
        manifest.Completed = true;
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, Json));
        if (!keepBackup)
        {
            Directory.Delete(backupDir, recursive: true);
        }
        else
        {
            report.Notes.Add($"backup kept: {backupDir}");
        }
    }

    private static string OnDiskRel(RepoPaths repo, string full)
    {
        string? dir = Path.GetDirectoryName(full);
        string? onDisk = dir == null ? null : Directory.EnumerateFiles(dir, Path.GetFileName(full)).FirstOrDefault();
        return repo.Rel(onDisk ?? full);
    }

    // Puts the tree back: created files go, saved files come back (with their original spelling).
    public static int Restore(string backupDir, TextWriter log)
    {
        string manifestPath = Path.Combine(backupDir, "backup-manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new ToolException($"{manifestPath} not found");
        }
        Manifest m = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), Json) ?? throw new ToolException("empty backup manifest");
        var repo = new RepoPaths(m.Repo);
        foreach (string rel in m.Created)
        {
            string full = repo.Full(rel);
            if (File.Exists(full))
            {
                File.Delete(full);
            }
        }
        foreach (string rel in m.Restore)
        {
            string src = Path.Combine(backupDir, "files", rel.Replace('/', Path.DirectorySeparatorChar));
            string dest = repo.Full(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest, overwrite: true);
        }
        // Directories the run created and left empty.
        foreach (string dir in m.Created.Select(r => Path.GetDirectoryName(repo.Full(r)) ?? "").Where(d => d.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(d => d.Length))
        {
            for (string? d = dir; d != null && repo.IsInside(d) && Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any(); d = Path.GetDirectoryName(d))
            {
                Directory.Delete(d);
            }
        }
        log.WriteLine($"restored {m.Restore.Count} files, removed {m.Created.Count} created files under {m.Repo}");
        return 0;
    }

    private static void ApplyWrites(RepoPaths repo, List<PendingWrite> writes, RunReport report)
    {
        // New content first, then delete the sources of moves, then remove directories the moves emptied.
        var deleted = new List<string>();
        foreach (PendingWrite w in writes.Where(w => w.Text != null))
        {
            if (w.DeleteFull != null && string.Equals(w.DeleteFull, w.OutFull, StringComparison.OrdinalIgnoreCase) && w.DeleteFull != w.OutFull)
            {
                File.Delete(w.DeleteFull); // case-only rename
            }
            TextFileData.Write(w.OutFull, w.Text!, w.Bom);
        }
        foreach (PendingWrite w in writes.Where(w => w.DeleteFull != null && (w.Text == null || !string.Equals(w.DeleteFull, w.OutFull, StringComparison.OrdinalIgnoreCase))))
        {
            File.Delete(w.DeleteFull!);
            deleted.Add(w.DeleteFull!);
        }
        foreach (string? dir in deleted.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(d => d!.Length))
        {
            for (string? d = dir; d != null && repo.IsInside(d) && RepoPaths.IsEditable(repo.Rel(d) + "/x"); d = Path.GetDirectoryName(d))
            {
                if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any())
                {
                    Directory.Delete(d);
                    report.FileOps.Add(new FileOp { Op = "rmdir", From = repo.Rel(d) });
                }
                else
                {
                    break;
                }
            }
        }
    }
}
