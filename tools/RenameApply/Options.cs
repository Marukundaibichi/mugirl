namespace RenameApply;

public sealed class Options
{
    public string Repo { get; set; } = "";
    public List<string> Specs { get; } = new();
    public string? ReportPath { get; set; }
    public bool DryRun { get; set; }
    public bool Force { get; set; }
    public bool Quiet { get; set; }
    public string? FrozenNamesPath { get; set; }
    public string? DumpDir { get; set; }
    public string? AppliedPath { get; set; }
    public string? BackupDir { get; set; }
    public bool KeepBackup { get; set; }
    public string? RestoreDir { get; set; }

    // Game folder for the Def-shape analysis (vanilla abstract parents): --game, else the RimWorldDir build property.
    public string? GameDir { get; set; }
    public string? EffectiveGameDir => GameDir ?? MsBuildProperties.Select(p => p.Split('=', 2)).Where(p => p.Length == 2 && p[0] == "RimWorldDir").Select(p => p[1]).LastOrDefault();
    public List<string> MsBuildProperties { get; } = new();

    // String literals in these files are never auto-replaced (they hold old names on purpose); mentions are reported as info.
    public List<string> LiteralExcludes { get; } = new()
    {
        "Source/MugirlRace/SaveUpgrade/**",
        "Source/Tests/**/SaveUpgrade/**",
        "Source/Tests/**/Fixtures/**",
        "Source/Tests/MugirlRace.Validation/Controls/**",
        "Source/Tests/MugirlRace.GameValidation/OfflineTests/**",
        "Source/Tests/**/Compatibility/*SaveMigrationTests.cs",
    };

    // Baselines whose entries may name OLD things on purpose (bans, drops): changes are reported, never applied.
    public List<string> ReportOnlyBaselines { get; } = new()
    {
        "Source/Tests/MugirlRace.Validation/Baselines/forbidden-patterns.txt",
        "Source/Tests/MugirlRace.Validation/Baselines/dropped-*.txt",
        // Legacy save schema: these list the names the frozen 1.6 build writes into saves (replaced by C.save-upgrade).
        "Source/Tests/MugirlRace.Validation/Baselines/save-types.txt",
        "Source/Tests/MugirlRace.Validation/Baselines/save-enums.txt",
        // 棘轮键和模块归属引用永久旧版语料，不跟随当前类型或 Def 的改名。
        "Source/Tests/MugirlRace.Validation/Baselines/save-upgrade-ratchet.*.txt",
        "Source/Tests/MugirlRace.Validation/Baselines/save-upgrade-modules.tsv",
    };

    public const string Usage = """
        RenameApply --repo <root> --spec <file|dir> [--spec ...] --report <report.json> [--dry-run]
                    [--msbuild-property Name=Value]... [--literal-exclude <glob>]... [--report-only-baseline <glob>]...
                    [--no-default-excludes] [--frozen-names <json>] [--dump <dir>] [--applied <json>] [--game <dir>]
                    [--backup <dir>] [--keep-backup] [--force] [--quiet]
        RenameApply --restore <backup dir>

          --spec                 merged spec, a module spec, an array of specs, or a directory of *.json specs (union)
          --dry-run              compute and verify everything, print the full change list, write only the report
          --msbuild-property     passed to the design-time builds (e.g. RimWorldDir=..., WorkshopDir=... for a copy)
          --dump                 also write every would-be-written file under <dir> (mirrored repo paths), even on failure
          --applied              write the applied log (maps only; input of the upgrader's generated tables) after a real run
          --frozen-names         spec/final/public-api.json (or a frozen-names.json); default: the copy next to the tool
          --game                 RimWorld folder for Def-shape analysis (default: the RimWorldDir build property)
          --backup               where a real run first copies every file it will overwrite or delete (default %TEMP%/RenameApply/backup-<time>);
                                 any failure while writing restores the tree from it; deleted after success unless --keep-backup
          --restore              put a tree back from a backup folder left by an interrupted run (its backup-manifest.json)
          --force                write even when verification failed (debugging only)
        Exit codes: 0 ok, 1 errors (nothing written unless --force), 2 usage/spec errors, 3 environment/internal error.
        """;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        bool defaults = true;
        var literal = new List<string>();
        var reportOnly = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ToolException($"missing value after {a}");
            switch (a)
            {
                case "--repo": o.Repo = Next(); break;
                case "--spec": o.Specs.Add(Next()); break;
                case "--report": o.ReportPath = Next(); break;
                case "--dry-run": o.DryRun = true; break;
                case "--force": o.Force = true; break;
                case "--quiet": o.Quiet = true; break;
                case "--dump": o.DumpDir = Next(); break;
                case "--applied": o.AppliedPath = Next(); break;
                case "--backup": o.BackupDir = Next(); break;
                case "--keep-backup": o.KeepBackup = true; break;
                case "--restore": o.RestoreDir = Next(); break;
                case "--game": o.GameDir = Next(); break;
                case "--frozen-names": o.FrozenNamesPath = Next(); break;
                case "--msbuild-property": o.MsBuildProperties.Add(Next()); break;
                case "--literal-exclude": literal.Add(Next()); break;
                case "--report-only-baseline": reportOnly.Add(Next()); break;
                case "--no-default-excludes": defaults = false; break;
                case "-h":
                case "--help":
                    throw new ToolException(Usage);
                default:
                    throw new ToolException($"unknown argument {a}\n{Usage}");
            }
        }
        if (!defaults)
        {
            o.LiteralExcludes.Clear();
            o.ReportOnlyBaselines.Clear();
        }
        o.LiteralExcludes.AddRange(literal);
        o.ReportOnlyBaselines.AddRange(reportOnly);
        if (o.RestoreDir != null)
        {
            return o;
        }
        if (o.Repo.Length == 0 || o.Specs.Count == 0 || o.ReportPath == null)
        {
            throw new ToolException("--repo, --spec and --report are required\n" + Usage);
        }
        return o;
    }
}
