using System.Diagnostics;
using System.Text;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Metadata;
using Mono.Cecil;

namespace MugirlRace.Validation.Legacy;

/// <summary>
/// <c>--extract-legacy-schema</c>：从驱动脚本列出的全部旧构建、内容树与真实存档提取旧存档名字语料，写入（或用 <c>--verify</c> 逐字节核对）
/// <c>Baselines/legacy/</c>。只读：输入目录、存档与参考程序集都不写。
/// </summary>
/// <remarks>
/// <para>
/// 语料对输入只追加：本次不再存在的输入（被覆盖的自动存档、更新了的创意工坊版本）的行与 builds.tsv 记录都保留，同一事实的聚合列
/// 取并集。提取器版本（<see cref="LegacyCorpus.ExtractorVersion"/>）不同的语料不合并，必须用 <c>--rewrite</c> 重写并审阅差异：
/// 派生事实的规则修正后，错误的旧行才能消失。
/// </para>
/// <para>
/// <c>--verify</c> 只用 builds.tsv 中记录过的输入（新输入只报告），要求它们的 sha256 不变；记录的输入都在时要求重新提取逐字节相同，
/// 有输入不在时与已有文件合并后逐字节相同。
/// </para>
/// </remarks>
internal static class LegacyExtraction
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // 依赖 Mod（HAR、Facial Animation）：它们的 defName 不是本 Mod 的名字。
    private static readonly string[] DependencyWorkshopIds = ["839005762", "1635901197"];

    public static int Run(ValidationOptions options)
    {
        if (options.LegacyInputs is null)
        {
            throw new UsageException("--extract-legacy-schema needs --inputs <tsv> (written by tools/Export-LegacySchema.ps1).");
        }

        string outDir = options.LegacyOut ?? Path.Combine(options.BaselineDir, "legacy");
        int? version = LegacyCorpus.VersionOf(outDir);
        if (version is { } existing && existing != LegacyCorpus.ExtractorVersion && !options.Rewrite)
        {
            Console.WriteLine($"The corpus in {outDir} was written by extractor version {existing}; this validator writes version {LegacyCorpus.ExtractorVersion}. "
                + "Derived facts changed, so the corpus is not merged: rerun with -Rewrite (--rewrite) and review the diff before committing it.");
            return ExitCodes.Failed;
        }

        Dictionary<string, BuildInfo> recorded = version is null || options.Rewrite ? new(StringComparer.Ordinal) : ReadBuilds(outDir);
        IReadOnlyList<BuildInput> inputs = BuildInput.ReadAll(options.LegacyInputs);
        if (options.Verify)
        {
            int unlisted = inputs.Count(input => !recorded.ContainsKey(input.Id));
            if (unlisted > 0)
            {
                Console.WriteLine($"  {unlisted} input(s) not recorded in builds.tsv are not verified (saves without the mod are never recorded; "
                    + "new builds or saves need tools/Export-LegacySchema.ps1).");
            }

            inputs = inputs.Where(input => recorded.ContainsKey(input.Id)).ToList();
        }

        Console.WriteLine($"Legacy schema extraction (extractor version {LegacyCorpus.ExtractorVersion}): {inputs.Count} input(s) -> {outDir}"
            + (options.Verify ? " (verify)" : options.Rewrite ? " (rewrite)" : ""));
        var timer = Stopwatch.StartNew();

        string? game16 = options.Ref16.Select(dir => Path.Combine(dir, "Assembly-CSharp.dll")).FirstOrDefault(File.Exists)
            ?? throw new EnvironmentException("--ref16 must name a folder with a full RimWorld 1.6 Assembly-CSharp.dll (method bodies are needed to derive save roots).");
        List<string> extraDirs = options.Ref16.Where(dir => !File.Exists(Path.Combine(dir, "Assembly-CSharp.dll"))).ToList();
        using TypeUniverse u16 = TypeUniverse.Create("1.6", game16, options.Ref16, redirectUnity: false);
        using TypeUniverse u17 = TypeUniverse.Create("1.7", Path.Combine(options.GameDir, "Assembly-CSharp.dll"), extraDirs, redirectUnity: true);
        Console.WriteLine($"  save roots: 1.6 {u16.GameRoots.Count}, 1.7 {u17.GameRoots.Count} (derived from Assembly-CSharp IL)");

        var portable = new PortablePaths(options);
        IReadOnlyList<CorpusTable> tables = LegacyCorpus.NewTables();
        Dictionary<string, CorpusTable> byFile = tables.ToDictionary(t => t.FileName, StringComparer.Ordinal);
        var builds = new List<BuildInfo>();
        var problems = new List<string>();
        var cache = new Dictionary<string, BuildFacts>(StringComparer.Ordinal);

        // 1. 构建（按首次出现时间）：先全部提取，Keyed 键的并集确定之后再写入表格。
        var extracted = new List<(BuildInfo Build, BuildFacts Facts)>();
        foreach (BuildInput input in inputs.Where(input => input.Kind != "save").OrderBy(input => input.FirstSeen, StringComparer.Ordinal).ThenBy(input => input.Id, StringComparer.Ordinal))
        {
            BuildFacts facts = Extract(input, u16, u17, cache, problems);
            BuildInfo build = facts.Describe(input);
            builds.Add(build);
            extracted.Add((build, facts));
            Console.WriteLine($"  {build.Id,-28} {build.Tier,-9} {build.Kind,-11} {build.Era,-14} types {facts.RowsOf(LegacyCorpus.TypesFile).Count,4}, labels {facts.RowsOf(LegacyCorpus.LabelsFile).Count,4}, defs {facts.RowsOf(LegacyCorpus.DefsFile).Count,4}");
        }

        // 只有程序集的输入（备份 DLL）没有 Keyed 文件：它们存进字符串字段的字面量按全部内容树的键判断是否为翻译键。
        var allKeyedKeys = new HashSet<string>(extracted.SelectMany(pair => pair.Facts.KeyedKeys), StringComparer.Ordinal);
        var knownTypes = new HashSet<string>(StringComparer.Ordinal);
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        var knownDefs = new HashSet<string>(StringComparer.Ordinal);
        var knownThingDefs = new HashSet<string>(StringComparer.Ordinal);
        var labels = new ObservedLabelClassifier();
        foreach ((BuildInfo build, BuildFacts facts) in extracted)
        {
            foreach ((string file, List<string[]> rows) in facts.Rows)
            {
                foreach (string[] row in rows)
                {
                    bool keyed = file == LegacyCorpus.SavedValuesFile && !facts.HasContent && row[2] == "literal" && allKeyedKeys.Contains(Unquote(row[1]));
                    byFile[file].Add(keyed ? [row[0], row[1], "keyed"] : row, build);
                }
            }

            foreach (string[] row in facts.RowsOf(LegacyCorpus.TypesFile))
            {
                knownTypes.Add(row[0]);
                if (row[0].LastIndexOf('.') is var dot and > 0)
                {
                    namespaces.Add(row[0][..dot].Split('+')[0]);
                }
            }

            foreach (string[] row in facts.RowsOf(LegacyCorpus.DefsFile))
            {
                knownDefs.Add(row[1]);
                if (row[0] == "ThingDef")
                {
                    knownThingDefs.Add(row[1]);
                }
            }

            labels.AddBuild(facts.RowsOf(LegacyCorpus.LabelsFile), facts.Chains, facts.DynamicOwners, facts.RowsOf(LegacyCorpus.TypesFile));
        }

        // 2. 真实存档：只保留本 Mod 的名字。原版、DLC 与依赖的 defName 与标签不属于本 Mod（旧内容可能覆盖原版 Def）。
        HashSet<string> foreignDefs = ForeignDefNames.Collect(
            new[] { Path.Combine(options.GameDir, "Data") }.Concat(DependencyWorkshopIds.Select(id => Path.Combine(options.WorkshopDir, id))));
        knownDefs.ExceptWith(foreignDefs);
        knownThingDefs.ExceptWith(foreignDefs);
        labels.AddForeignLabels(u16.ScribeLabels);
        labels.AddForeignLabels(u17.ScribeLabels);
        Console.WriteLine($"  foreign names: {foreignDefs.Count} game/dependency defName(s), {u16.ScribeLabels.Count + u17.ScribeLabels.Count} literal Scribe label(s) of 1.6/1.7 and dependencies");

        var scanner = new SaveTokenScanner(knownTypes, namespaces, knownDefs, knownThingDefs);
        int skippedSaves = 0;
        foreach (BuildInput input in inputs.Where(input => input.Kind == "save").OrderBy(input => input.FirstSeen, StringComparer.Ordinal).ThenBy(input => input.Id, StringComparer.Ordinal))
        {
            SaveScan scan = scanner.Scan(input.File);
            if (!scan.HasMod)
            {
                skippedSaves++;
                continue;
            }

            var build = new BuildInfo(input.Id, input.Tier, "save", input.Source, LegacyHash.Sha256File(input.File), "-", "-", scan.Era,
                SaveTokenScanner.PackageId, input.FirstSeen, input.LastSeen, scan.GameVersion.Length == 0 ? "-" : "game " + scan.GameVersion);
            builds.Add(build);
            int count = 0;
            foreach ((string kind, string token, string context) in scan.Tokens)
            {
                IEnumerable<(string Kind, string Token, string Context)> rows = kind == "label" ? labels.Classify(token, context) : [(kind, token, context)];
                foreach ((string rowKind, string rowToken, string rowContext) in rows)
                {
                    byFile[LegacyCorpus.ObservedFile].Add([rowKind, rowToken, rowContext], build);
                    count++;
                }
            }

            Console.WriteLine($"  {build.Id,-28} {build.Tier,-9} save        {build.Era,-14} {count} token(s), {scan.GameVersion}");
        }

        if (skippedSaves > 0)
        {
            problems.Add($"{skippedSaves} save(s) without {SaveTokenScanner.PackageId} in meta/modIds were skipped.");
        }

        foreach (TypeUniverse universe in new[] { u16, u17 })
        {
            foreach ((string path, string sha) in universe.Inputs)
            {
                AssemblyFacts facts = AssemblyFacts.Read(path);
                builds.Add(new BuildInfo($"r.{universe.Label}.{facts.Name}", "ref", "ref", portable.Of(path), sha, "-", facts.Name, "-", "-", "-", "-",
                    "version " + facts.Version));
            }
        }

        foreach (string problem in problems)
        {
            Console.WriteLine("  note: " + problem);
        }

        Console.WriteLine($"  extraction took {timer.Elapsed.TotalSeconds:F1} s; " + string.Join(", ", tables.Select(t => $"{t.FileName} {t.Count}")));
        var current = builds.ToDictionary(b => b.Id, StringComparer.Ordinal);
        List<BuildInfo> missing = recorded.Values.Where(build => !current.ContainsKey(build.Id)).ToList();
        if (options.Verify)
        {
            return Verify(outDir, recorded, current, missing, tables, builds);
        }

        if (options.Rewrite && version is not null)
        {
            foreach (BuildInfo lost in ReadBuilds(outDir).Values.Where(build => !current.ContainsKey(build.Id)))
            {
                Console.WriteLine($"  rewrite drops input {lost.Id} ({lost.Source}): it is no longer available, its facts are not re-derived.");
            }
        }

        Dictionary<string, string> files = Render(outDir, tables, builds, missing, merge: !options.Rewrite, out int retained);
        int written = Write(outDir, files, retained);
        return options.Expect is { } expect ? CheckExpectations(outDir, expect) : written;
    }

    // 提取器的对照（Controls/Legacy/expected-corpus.txt）：每行 文件<TAB>present|absent<TAB>行的子串（制表符写成 \t）[<TAB>说明]。
    private static int CheckExpectations(string outDir, string path)
    {
        int problems = 0;
        int rules = 0;
        foreach (string line in File.ReadLines(path).Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0 && !l.StartsWith('#')))
        {
            string[] parts = line.Split('\t');
            if (parts.Length < 3 || parts[1] is not ("present" or "absent"))
            {
                throw new UsageException($"{path}: '{line}' needs file<TAB>present|absent<TAB>substring.");
            }

            rules++;
            string file = Path.Combine(outDir, parts[0]);
            string needle = parts[2].Replace("\\t", "\t", StringComparison.Ordinal);
            bool found = File.Exists(file) && LegacyCorpus.DataLines(file).Any(data => data.Contains(needle, StringComparison.Ordinal));
            if (found != (parts[1] == "present"))
            {
                problems++;
                Console.WriteLine($"EXTRACTION CONTROL failed: {parts[0]} should {(parts[1] == "present" ? "contain" : "not contain")} '{parts[2]}'" + (parts.Length > 3 ? $" ({parts[3]})" : ""));
            }
        }

        Console.WriteLine(problems == 0 ? $"EXTRACTION CONTROLS PASSED: {rules} expectation(s) met." : $"EXTRACTION CONTROLS FAILED: {problems} of {rules} expectation(s) not met.");
        return problems == 0 ? ExitCodes.Passed : ExitCodes.Failed;
    }

    private static BuildFacts Extract(BuildInput input, TypeUniverse u16, TypeUniverse u17, Dictionary<string, BuildFacts> cache, List<string> problems)
    {
        string? sha = input.HasFile ? LegacyHash.Sha256File(input.File) : null;
        string? fingerprint = input.HasRoot ? string.Join("|", ContentSchema.LoadFolders(input.Root, "1.6").Select(folder => Path.GetRelativePath(input.Root, folder))) + "|" + ContentFingerprint(input.Root) : null;
        string key = (sha ?? "-") + "|" + (fingerprint ?? "-");
        if (cache.TryGetValue(key, out BuildFacts? cached))
        {
            return cached;
        }

        AssemblyFacts? identity = input.HasFile ? AssemblyFacts.Read(input.File) : null;
        TypeUniverse universe = identity?.TargetFramework?.StartsWith(".NETCoreApp", StringComparison.Ordinal) == true ? u17 : u16;
        ModuleDefinition? module = input.HasFile ? universe.ReadMod(input.File) : null;
        try
        {
            ContentSchema? content = input.HasRoot ? ContentSchema.Read(input.Root, "1.6", universe, module) : null;
            foreach (string problem in content?.Problems ?? [])
            {
                problems.Add($"{input.Id}: {problem}");
            }

            AssemblySchema? assembly = module is null ? null : AssemblySchema.Analyze(module, universe.GameRoots, content, content?.KeyedKeys);
            var rows = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
            if (assembly is not null)
            {
                rows[LegacyCorpus.TypesFile] = assembly.Types;
                rows[LegacyCorpus.LabelsFile] = assembly.Labels;
                rows[LegacyCorpus.GraphFile] = assembly.Graph;
                rows[LegacyCorpus.EnumsFile] = assembly.Enums;
                rows[LegacyCorpus.SavedValuesFile] = assembly.SavedValues;
                rows[LegacyCorpus.LoadIdsFile] = assembly.LoadIds;
                rows[LegacyCorpus.SettingsFile] = assembly.Settings;
                rows[LegacyCorpus.JobsLordsFile] = assembly.JobsAndLords;
            }

            if (content is not null)
            {
                rows[LegacyCorpus.DefsFile] = content.Defs;
                rows[LegacyCorpus.DefCompsFile] = content.DefComps;
                rows[LegacyCorpus.XmlTypesFile] = content.XmlTypes;
                rows[LegacyCorpus.ToolsFile] = content.Tools;
            }

            var facts = new BuildFacts(identity, sha, input.HasFile ? LegacyHash.GitBlob(File.ReadAllBytes(input.File)) : null, universe.Label,
                assembly is not null, content?.PackageId, content?.ContentRef, rows)
            {
                HasContent = content is not null,
                KeyedKeys = content?.KeyedKeys.ToList() ?? [],
                Chains = assembly?.Chains ?? new Dictionary<string, List<string>>(StringComparer.Ordinal),
                DynamicOwners = assembly?.DynamicOwners.ToList() ?? [],
            };
            cache[key] = facts;
            return facts;
        }
        finally
        {
            module?.Dispose();
        }
    }

    // 内容指纹（只看文件名、大小与修改时间之外的内容：路径与 sha256），用来复用相同内容树的提取结果。
    private static string ContentFingerprint(string root)
    {
        var lines = new List<string>();
        foreach (string folder in ContentSchema.LoadFolders(root, "1.6"))
        {
            foreach (string sub in new[] { "Defs", "Patches", "Languages" })
            {
                string dir = Path.Combine(folder, sub);
                if (Directory.Exists(dir))
                {
                    foreach (string file in Directory.EnumerateFiles(dir, "*.xml", SearchOption.AllDirectories))
                    {
                        lines.Add(Path.GetRelativePath(root, file).Replace('\\', '/') + "\0" + LegacyHash.Sha256File(file));
                    }
                }
            }
        }

        lines.Sort(StringComparer.Ordinal);
        return LegacyHash.Sha256(string.Join("\n", lines));
    }

    private static Dictionary<string, BuildInfo> ReadBuilds(string outDir)
    {
        var result = new Dictionary<string, BuildInfo>(StringComparer.Ordinal);
        string path = Path.Combine(outDir, LegacyCorpus.BuildsFile);
        if (File.Exists(path))
        {
            foreach (string line in LegacyCorpus.DataLines(path))
            {
                if (BuildInfo.Parse(line) is { } build)
                {
                    result.TryAdd(build.Id, build);
                }
            }
        }

        return result;
    }

    // builds.tsv 的顺序：构建、存档、参考程序集各自按首次出现时间（再按 id）；不再存在但已记录的输入按同样的规则排进去。
    private static List<BuildInfo> Order(IEnumerable<BuildInfo> builds)
        => builds.OrderBy(build => build.Kind switch { "save" => 1, "ref" => 2, _ => 0 })
            .ThenBy(build => build.FirstSeen, StringComparer.Ordinal).ThenBy(build => build.Id, StringComparer.Ordinal).ToList();

    // 每个文件的完整内容（文件头 + 数据行）。merge 时把已有文件中的行合并进来（只追加，聚合列取并集）。
    private static Dictionary<string, string> Render(string outDir, IReadOnlyList<CorpusTable> tables, List<BuildInfo> builds, List<BuildInfo> missing, bool merge, out int retained)
    {
        retained = 0;
        List<BuildInfo> ordered = Order(merge ? builds.Concat(missing) : builds);
        foreach (BuildInfo build in merge ? missing : [])
        {
            Console.WriteLine($"  retained (append-only): {LegacyCorpus.BuildsFile} {build.Id} ({build.Source})");
        }

        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < ordered.Count; i++)
        {
            order[ordered[i].Id] = i;
        }

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [LegacyCorpus.BuildsFile] = Join(
                ["# builds.tsv: every input of the legacy corpus (builds, then saves, then reference assemblies, each in first-seen order; the first/last columns of the other files refer to this order).",
                 "# Generated by MugirlRace.Validation --extract-legacy-schema (tools/Export-LegacySchema.ps1); append-only, never edit by hand.",
                 LegacyCorpus.VersionPrefix + LegacyCorpus.ExtractorVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                 "# columns: " + string.Join('\t', BuildInfo.Columns)],
                ordered.Select(build => build.ToLine())),
        };

        foreach (CorpusTable table in tables)
        {
            string path = Path.Combine(outDir, table.FileName);
            if (merge && File.Exists(path))
            {
                foreach (string line in LegacyCorpus.DataLines(path))
                {
                    if (table.Merge(line))
                    {
                        retained++;
                    }
                }
            }

            files[table.FileName] = Join(table.Header(), table.Render(order));
        }

        return files;
    }

    private static string Join(IEnumerable<string> header, IEnumerable<string> lines)
        => string.Join("\n", header.Concat(lines)) + "\n";

    private static int Write(string outDir, Dictionary<string, string> files, int retained)
    {
        Directory.CreateDirectory(outDir);
        foreach ((string name, string content) in files)
        {
            File.WriteAllText(Path.Combine(outDir, name), content, Utf8);
        }

        string extra = Path.Combine(outDir, LegacyCorpus.ExtraFile);
        if (!File.Exists(extra))
        {
            File.WriteAllText(extra, "# legacy-extra.tsv: names known only from evidence (no build or save in builds.tsv contains them). Hand-maintained.\n"
                + "# columns: kind\tname\tcontext\tevidence (kind as in legacy-observed.tsv; evidence = path or document that shows the name)\n", Utf8);
        }

        Console.WriteLine($"Wrote {files.Count} corpus file(s) to {outDir}" + (retained > 0 ? $" ({retained} line(s) retained from the previous corpus)" : ""));
        return ExitCodes.Passed;
    }

    private static int Verify(string outDir, Dictionary<string, BuildInfo> recorded, Dictionary<string, BuildInfo> current, List<BuildInfo> missing,
        IReadOnlyList<CorpusTable> tables, List<BuildInfo> builds)
    {
        int problems = 0;
        foreach (BuildInfo build in recorded.Values)
        {
            if (!current.TryGetValue(build.Id, out BuildInfo? now))
            {
                Console.WriteLine($"  note: recorded input {build.Id} ({build.Source}) is no longer available; its lines are kept (append-only).");
            }
            else if (now.Sha256 != build.Sha256)
            {
                Console.WriteLine($"VERIFY: input {build.Id} changed: sha256 {now.Sha256} instead of {build.Sha256} (an id must name one content).");
                problems++;
            }
        }

        // 记录的输入都在时必须原样复现（合并会掩盖提取结果变少）；有输入缺失时只能与已有文件合并后比较。
        Dictionary<string, string> files = Render(outDir, tables, builds, missing, merge: missing.Count > 0, out _);
        foreach ((string name, string content) in files)
        {
            string path = Path.Combine(outDir, name);
            string existing = File.Exists(path) ? File.ReadAllText(path, Utf8) : "";
            if (!string.Equals(existing, content, StringComparison.Ordinal) || (File.Exists(path) && HasBom(path)))
            {
                Console.WriteLine($"VERIFY: {name} differs from a fresh extraction{(missing.Count > 0 ? " merged with the recorded lines" : "")}.");
                problems++;
            }
        }

        Console.WriteLine(problems == 0 ? $"VERIFY PASSED: {files.Count} corpus file(s) reproduced byte for byte ({missing.Count} recorded input(s) unavailable)." : $"VERIFY FAILED: {problems} problem(s).");
        return problems == 0 ? ExitCodes.Passed : ExitCodes.Failed;
    }

    private static bool HasBom(string path)
    {
        using FileStream stream = File.OpenRead(path);
        Span<byte> bom = stackalloc byte[3];
        return stream.Read(bom) == 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF;
    }

    private static string Unquote(string value)
        => value.Length >= 2 && value[0] == '"' ? System.Text.Json.JsonSerializer.Deserialize<string>(value) ?? value : value;

    /// <summary>一个构建的提取结果（相同 DLL 与内容树的输入共用；只保留语料行与观测分类用的数据，不保留 Cecil 模块）。</summary>
    private sealed record BuildFacts(AssemblyFacts? Identity, string? Sha256, string? GitBlob, string Universe, bool HasAssembly, string? PackageId,
        string? ContentRef, Dictionary<string, List<string[]>> Rows)
    {
        public bool HasContent { get; init; }

        public IReadOnlyList<string> KeyedKeys { get; init; } = [];

        public IReadOnlyDictionary<string, List<string>> Chains { get; init; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        public IReadOnlyList<string> DynamicOwners { get; init; } = [];

        public IReadOnlyList<string[]> RowsOf(string file) => Rows.TryGetValue(file, out List<string[]>? rows) ? rows : [];

        public BuildInfo Describe(BuildInput input)
        {
            string assemblyName = Identity?.Name ?? "-";
            string packageId = PackageId is { Length: > 0 } id ? id : "-";
            string era = assemblyName is "MooGirlRace" or "WRace" || packageId == "HAR.MuGirlRace" ? "MooGirl"
                : assemblyName == "MugirlRace" || packageId.Equals("HAR.MugirlRace", StringComparison.Ordinal) ? "Mugirl"
                : "-";
            string kind = (HasAssembly, ContentRef is not null) switch
            {
                (true, true) => "dll+content",
                (true, false) => "dll",
                _ => "content",
            };
            string contentRef = (ContentRef is null ? "" : ContentRef + " ") + "game " + Universe;
            return new BuildInfo(input.Id, input.Tier, kind, input.Source, Sha256 ?? "-", GitBlob ?? "-", assemblyName, era, packageId,
                input.FirstSeen, input.LastSeen, contentRef);
        }
    }
}
