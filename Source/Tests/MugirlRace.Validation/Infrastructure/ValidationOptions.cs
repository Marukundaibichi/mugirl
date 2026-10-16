using System.Reflection;

namespace MugirlRace.Validation.Infrastructure;

/// <summary>命令行选项。默认值来自构建时写入的程序集元数据（见工程文件）。</summary>
internal sealed class ValidationOptions
{
    public const string Usage = """
        MugirlRace.Validation — offline checks of the RimWorld 1.7 build of Mugirl Race.

        Modes:
          (default)                Run every check against --mod and --mod-root.
          --extract-legacy-schema  Extract the legacy save-name corpus from the inputs listed by --inputs (written by
                                   tools/Export-LegacySchema.ps1) into --out (default: <baselines>/legacy). The corpus is
                                   append-only for inputs of the same extractor version. With --verify, re-extract the
                                   recorded inputs and require byte-identical files instead of writing; with --rewrite,
                                   regenerate the corpus from the available inputs (required after an extractor version change).
          --parity <A.dll> <B.dll> IL and content parity of two builds through a rename map (Wave R gate). Options:
                                   --map <json|tsv> (RenameApply report or kind<TAB>old<TAB>new), --content-a/--content-b
                                   <mod roots>, --parity-accept <tsv>, --expect <file> (negative-control expectations),
                                   --report <json>.
          --save-corpus <dir>      Read every *.rws and *.rws.gz below <dir> (read-only), run the upgrader's offline entry point
                                   on it twice and require: a known non-failed state, a second run without actions, at most
                                   2000 ms per save (decisions §2 budget).

        Options (all paths default to the locations recorded at build time):
          --mod <dll>              The MugirlRace.dll to validate (default: <mod-root>/1.7/Assemblies/MugirlRace.dll).
                                   Its portable PDB, when present next to it, maps methods to source files.
          --mod-root <dir>         The mod folder whose About, LoadFolders, Defs, Patches, Languages, Textures and Sounds
                                   are checked.
          --source <dir>           The C# project folder of the 1.7 build (BannedSymbols.txt, StaticCacheLifecycle comments)
                                   (default: <mod-root>/Source/MugirlRace).
          --game <dir>             RimWorld 1.7 installation.
          --harmony <dir>          Folder of the Harmony 3 0Harmony.dll the game loads for 1.7.
          --workshop <dir>         Steam workshop content folder (294100) used to find dependency mods.
          --baselines <dir>        Golden files and allow-lists (default: the project's Baselines folder).
          --legacy-corpus <dir>    The legacy corpus read by C.save-upgrade (default: <baselines>/legacy when it holds
                                   builds.tsv, else <mod-root>/Source/Tests/MugirlRace.Validation/Baselines/legacy).
          --save-upgrade <mode>    ratchet (default: an undisposed required corpus line fails only when it is not listed in
                                   Baselines/save-upgrade-ratchet.<module>.txt) or strict (every undisposed line fails).
          --ratchet-reset          Rewrite the save-upgrade ratchet files with the current undisposed lines (integrator only).
          --artifacts <dir>        Where C.save-upgrade writes save-upgrade-coverage.tsv and legacy-token-set.txt and
                                   B.patch-order writes the current order (default: next to --report, else no files).
          --assume-active <ids>    Treat these package ids (comma separated) as active, so IfModActive load folders and
                                   MayRequire content are checked; installed ones are loaded with their dependencies.
                                   "all" = every package id named by an IfModActive condition of the mod's LoadFolders.xml.
          --report <json>          Write the machine-readable report here.
          --update-baselines       Rewrite the generated golden lists (load-folders.txt, patch-targets.txt, patch-order.txt,
                                   transpiler-hooks.txt); rewrite unity-boundary.json (new boundary data, enums and
                                   Mugirl.Platform-only references get a "REVIEW:" reason, unused entries are removed;
                                   violations are never added); append new dynamic lookups to dynamic-lookups.allow.txt
                                   and new RS0030 suppression sites in Mugirl.Platform/Mathematics/MugirlLog to
                                   banned-symbol-exemptions.txt with a "REVIEW:" reason; remove disposed lines from the
                                   save-upgrade ratchet files. REVIEW entries are reported as warnings until a person
                                   replaces the reason. Other lists are never written.
          --inputs <tsv>           Extraction inputs (id, tier, kind, source, file, root, firstSeen, lastSeen).
          --out <dir>              Extraction output folder.
          --ref16 <dirs>           Semicolon-separated folders with the RimWorld 1.6 Assembly-CSharp.dll (full, with method
                                   bodies) and the 1.6 assemblies legacy XML names (HAR, Facial Animation, Harmony 2).
          --verify                 With --extract-legacy-schema: compare instead of writing.
          --rewrite                With --extract-legacy-schema: do not merge with the existing corpus (review the diff).
                                   --expect <file> checks the written corpus against file<TAB>present|absent<TAB>substring
                                   lines (extractor controls, Controls/Legacy).
          --frozen <dll>           Ignored (the frozen 1.6 build is part of the legacy corpus now); accepted until
                                   tools/Validate.ps1 stops passing it.
          --help                   Show this text.

        Exit codes: 0 all checks passed, 1 at least one check failed, 2 usage or environment error.
        """;

    private static readonly string[] ValueOptions =
    [
        "--mod", "--mod-root", "--frozen", "--source", "--game", "--harmony", "--workshop", "--baselines", "--assume-active", "--report",
        "--legacy-corpus", "--save-upgrade", "--artifacts", "--inputs", "--out", "--ref16", "--map", "--content-a", "--content-b",
        "--parity-accept", "--expect", "--save-corpus",
    ];

    private ValidationOptions()
    {
    }

    public ValidationMode Mode { get; private init; }

    public string ModRoot { get; private init; } = "";

    public string ModAssembly { get; private init; } = "";

    public string SourceDir { get; private init; } = "";

    public string GameDir { get; private init; } = "";

    public string HarmonyDir { get; private init; } = "";

    public string WorkshopDir { get; private init; } = "";

    public string BaselineDir { get; private init; } = "";

    /// <summary><c>--assume-active</c> 的包 id（原样保留，<c>all</c> 由 <c>ModCatalog</c> 展开）。</summary>
    public IReadOnlyList<string> AssumeActive { get; private init; } = [];

    public string? ReportPath { get; private init; }

    public bool UpdateBaselines { get; private init; }

    public bool ShowHelp { get; private init; }

    /// <summary><c>--legacy-corpus</c>；未给出时由 <c>C.save-upgrade</c> 按用法中的规则查找。</summary>
    public string? LegacyCorpus { get; private init; }

    /// <summary><c>--save-upgrade strict</c>：未处置的必需语料行一律失败，不看 ratchet 文件。</summary>
    public bool SaveUpgradeStrict { get; private init; }

    public bool RatchetReset { get; private init; }

    /// <summary>C.save-upgrade 与 B.patch-order 写生成文件的目录；未给出时用 <c>--report</c> 所在目录，两者都没有时为空。</summary>
    public string? ArtifactsDir { get; private init; }

    public string? LegacyInputs { get; private init; }

    public string? LegacyOut { get; private init; }

    public IReadOnlyList<string> Ref16 { get; private init; } = [];

    public bool Verify { get; private init; }

    /// <summary><c>--rewrite</c>：语料不与已有文件合并（提取器版本改变后重写）。</summary>
    public bool Rewrite { get; private init; }

    /// <summary><c>--parity A B</c> 的两个程序集。</summary>
    public (string A, string B)? Parity { get; private init; }

    public string? ParityMap { get; private init; }

    public string? ContentA { get; private init; }

    public string? ContentB { get; private init; }

    public string? ParityAccept { get; private init; }

    public string? Expect { get; private init; }

    public string? SaveCorpusDir { get; private init; }

    /// <summary>游戏本地 Mods 目录（依赖 Mod 也可能装在这里而不是创意工坊）。</summary>
    public string LocalModsDir => Path.Combine(GameDir, "Mods");

    /// <summary>被测程序集旁的便携式 PDB；不存在时为 <see langword="null"/>。</summary>
    public string? ModPdb => Path.ChangeExtension(ModAssembly, ".pdb") is { } pdb && File.Exists(pdb) ? pdb : null;

    public static ValidationOptions Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool update = false;
        bool help = false;
        bool extract = false;
        bool verify = false;
        bool reset = false;
        bool rewrite = false;
        (string, string)? parity = null;
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                case "/?":
                    help = true;
                    break;
                case "--update-baselines":
                    update = true;
                    break;
                case "--extract-legacy-schema":
                    extract = true;
                    break;
                case "--verify":
                    verify = true;
                    break;
                case "--ratchet-reset":
                    reset = true;
                    break;
                case "--rewrite":
                    rewrite = true;
                    break;
                case "--parity":
                    if (i + 2 >= args.Count)
                    {
                        throw new UsageException("Option --parity needs two assemblies: --parity <A.dll> <B.dll>.");
                    }

                    parity = (FullPath(args[i + 1]), FullPath(args[i + 2]));
                    i += 2;
                    break;
                default:
                    if (!ValueOptions.Contains(arg, StringComparer.Ordinal))
                    {
                        throw new UsageException($"Unknown argument '{arg}'.");
                    }

                    if (i + 1 >= args.Count)
                    {
                        throw new UsageException($"Option {arg} needs a value.");
                    }

                    values[arg] = args[++i];
                    break;
            }
        }

        if (help)
        {
            return new ValidationOptions { ShowHelp = true };
        }

        string? saveCorpus = values.TryGetValue("--save-corpus", out string? corpusDir) ? FullPath(corpusDir) : null;
        if ((extract ? 1 : 0) + (parity is null ? 0 : 1) + (saveCorpus is null ? 0 : 1) > 1)
        {
            throw new UsageException("--extract-legacy-schema, --parity and --save-corpus are separate modes; use one.");
        }

        if (verify && rewrite)
        {
            throw new UsageException("--verify and --rewrite exclude each other.");
        }

        string upgradeMode = values.GetValueOrDefault("--save-upgrade") ?? "ratchet";
        if (upgradeMode is not ("ratchet" or "strict"))
        {
            throw new UsageException("--save-upgrade takes 'ratchet' or 'strict'.");
        }

        string modRoot = FullPath(values.GetValueOrDefault("--mod-root") ?? BuildSetting("Mugirl.ModDir"));
        string? report = values.TryGetValue("--report", out string? reportPath) ? FullPath(reportPath) : null;
        return new ValidationOptions
        {
            Mode = extract ? ValidationMode.ExtractLegacySchema : parity is not null ? ValidationMode.Parity : saveCorpus is not null ? ValidationMode.SaveCorpus : ValidationMode.Validate,
            UpdateBaselines = update,
            ModRoot = modRoot,
            ModAssembly = FullPath(values.GetValueOrDefault("--mod") ?? Path.Combine(modRoot, "1.7", "Assemblies", "MugirlRace.dll")),
            SourceDir = FullPath(values.GetValueOrDefault("--source") ?? Path.Combine(modRoot, "Source", "MugirlRace")),
            GameDir = FullPath(values.GetValueOrDefault("--game") ?? BuildSetting("Mugirl.GameDir")),
            HarmonyDir = FullPath(values.GetValueOrDefault("--harmony") ?? BuildSetting("Mugirl.HarmonyDir")),
            WorkshopDir = FullPath(values.GetValueOrDefault("--workshop") ?? BuildSetting("Mugirl.WorkshopDir")),
            BaselineDir = FullPath(values.GetValueOrDefault("--baselines") ?? BuildSetting("Mugirl.BaselineDir")),
            AssumeActive = values.TryGetValue("--assume-active", out string? assumed)
                ? assumed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [],
            ReportPath = report,
            LegacyCorpus = values.TryGetValue("--legacy-corpus", out string? corpus) ? FullPath(corpus) : null,
            SaveUpgradeStrict = upgradeMode == "strict",
            RatchetReset = reset,
            ArtifactsDir = values.TryGetValue("--artifacts", out string? artifacts) ? FullPath(artifacts) : report is null ? null : Path.GetDirectoryName(report),
            LegacyInputs = values.TryGetValue("--inputs", out string? inputs) ? FullPath(inputs) : null,
            LegacyOut = values.TryGetValue("--out", out string? output) ? FullPath(output) : null,
            Ref16 = values.TryGetValue("--ref16", out string? ref16)
                ? ref16.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(FullPath).ToList()
                : [],
            Verify = verify,
            Rewrite = rewrite,
            Parity = parity,
            ParityMap = values.TryGetValue("--map", out string? map) ? FullPath(map) : null,
            ContentA = values.TryGetValue("--content-a", out string? contentA) ? FullPath(contentA) : null,
            ContentB = values.TryGetValue("--content-b", out string? contentB) ? FullPath(contentB) : null,
            ParityAccept = values.TryGetValue("--parity-accept", out string? accept) ? FullPath(accept) : null,
            Expect = values.TryGetValue("--expect", out string? expect) ? FullPath(expect) : null,
            SaveCorpusDir = saveCorpus,
        };
    }

    private static string FullPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string BuildSetting(string key)
    {
        foreach (AssemblyMetadataAttribute attribute in typeof(ValidationOptions).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (attribute.Key == key && !string.IsNullOrEmpty(attribute.Value))
            {
                return attribute.Value;
            }
        }

        throw new UsageException($"No default for {key}: pass the corresponding option.");
    }
}
