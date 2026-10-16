using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml;
using MugirlRace.Validation.Content;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Legacy;
using MugirlRace.Validation.Metadata;
using MugirlRace.Validation.Reporting;
using MugirlRace.Validation.Runtime;
using MugirlRace.Validation.SaveUpgrade;
using Mono.Cecil;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// C. 旧存档的每个名字都有处置：<c>Baselines/legacy/</c> 语料（全部旧构建与真实存档）对照新构建（Cecil、字符串流）、升级清单
/// （运行时按名调用 <c>Mugirl.SaveUpgrade.SaveUpgradeManifest.ToJson()</c>，契约 K1；它执行 Mod 代码，所以在第 4 阶段运行）与内容模型。
/// 见 <see cref="DispositionEngine"/>。
/// </summary>
/// <remarks>
/// <para>
/// ratchet 模式（默认，Wave R 到 S）：未处置的必需行（published/observed）只在不在 <c>Baselines/save-upgrade-ratchet.&lt;module&gt;.txt</c>
/// 中时失败；已处置但仍列出的行报警告，<c>--update-baselines</c> 删去它们。strict 模式（<c>--save-upgrade strict</c>，Wave V）每个
/// 未处置的必需行都失败。清单卫生问题在两种模式下都失败。
/// </para>
/// <para>
/// 输出（<c>--artifacts</c>）：<c>save-upgrade-coverage.tsv</c>（每条语料行的处置，审阅视图）与 <c>legacy-token-set.txt</c>
/// （只属于旧构建的名字，供重存扫描与 Wave R 残留扫描使用）。
/// </para>
/// </remarks>
internal static class SaveUpgradeCheck
{
    public const string ManifestType = "Mugirl.SaveUpgrade.SaveUpgradeManifest";
    public const string StampType = "Mugirl.Core.GameComponent_Mugirl";
    public const string StampLabel = "mugirlSaveSchema";
    public const string ModClassShortName = "MugirlMod";
    public const string RatchetPrefix = "save-upgrade-ratchet.";
    public const string CoverageFile = "save-upgrade-coverage.tsv";
    public const string TokenSetFile = "legacy-token-set.txt";
    private const string ManifestMissingKey = "manifest|missing";
    private const string ManifestModule = "saveupgrade";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static void Run(Report report, ValidationOptions options, MetadataWorld world, ModuleStringFlow flow, GameRuntime runtime, ContentModel content)
        => report.Run("C.save-upgrade", "Every legacy save name is unchanged, renamed, consumed by a verified step or dropped (" + (options.SaveUpgradeStrict ? "strict" : "ratchet") + ")",
            recorder => Check(recorder, options, world, flow, runtime, content));

    /// <summary>语料目录：<c>--legacy-corpus</c>；否则 <c>&lt;baselines&gt;/legacy</c>（有 builds.tsv 时）；否则 Mod 源码树中的验证程序基线。</summary>
    public static string CorpusDirectory(ValidationOptions options)
    {
        if (options.LegacyCorpus is { } explicitDir)
        {
            return explicitDir;
        }

        string inBaselines = Path.Combine(options.BaselineDir, "legacy");
        return File.Exists(Path.Combine(inBaselines, LegacyCorpus.BuildsFile))
            ? inBaselines
            : Path.Combine(options.ModRoot, "Source", "Tests", "MugirlRace.Validation", "Baselines", "legacy");
    }

    private static void Check(CheckRecorder recorder, ValidationOptions options, MetadataWorld world, ModuleStringFlow flow, GameRuntime runtime, ContentModel content)
    {
        string corpusDir = CorpusDirectory(options);
        if (LegacyCorpus.Load(corpusDir) is not { } corpus)
        {
            recorder.Fail("Baselines/legacy", $"Legacy corpus missing at {corpusDir}: run tools/Export-LegacySchema.ps1.");
            return;
        }

        foreach (string problem in corpus.Problems)
        {
            recorder.Fail("Baselines/legacy", problem);
        }

        ModuleDefinition game = world.Read(Path.Combine(options.GameDir, "Assembly-CSharp.dll"));
        SaveRoots gameRoots = SaveRoots.Derive([game]);
        var xml = new ContentFieldValues(content);
        var keyed = new HashSet<string>(StringComparer.Ordinal);
        AssemblySchema schema = AssemblySchema.Analyze(flow, gameRoots, xml, keyed);
        var build = new NewBuild(world.Mod, schema, content, content.Catalog.UnderTest.RootDir);

        UpgradeManifest? manifest = ReadManifest(recorder, runtime);
        if (manifest is not null)
        {
            CheckStamp(recorder, manifest, build);
        }

        if (!build.ModClassShortNames.Contains(ModClassShortName))
        {
            recorder.Fail("frozen: Mod class", $"No Verse.Mod subclass named {ModClassShortName}: the settings file name Mod_<folder>_{ModClassShortName}.xml is frozen forever.");
        }

        ModuleMap modules = ModuleMap.Load(options.BaselineDir);
        foreach (string problem in modules.Problems)
        {
            recorder.Fail("Baselines/" + ModuleMap.FileName, problem);
        }

        var golden = new GoldenSteps(Path.Combine(options.ModRoot, "Source", "Tests", "MugirlRace.Tests", "SaveUpgrade", "Golden"));
        var engine = new DispositionEngine(corpus, build, manifest, golden, modules, PublicApiCheck.TypeNames(options));
        engine.UseSources(type => world.Sources.DocumentsOf(type).Select(d => SourceMap.ProjectRelative(d, options.SourceDir)).OfType<string>().Order(StringComparer.Ordinal).FirstOrDefault());
        engine.Run();
        foreach ((string subject, string message) in engine.Failures)
        {
            recorder.Fail(subject, message);
        }

        List<CoverageRow> coverage = engine.Coverage;
        if (manifest is null)
        {
            coverage.Add(new CoverageRow(ManifestMissingKey, "published", "undisposed", "", "", ManifestModule,
                $"{ManifestType}.ToJson() is not in the build (contract K1); nothing can be renamed, consumed or dropped yet."));
        }

        Ratchet(recorder, options, coverage);
        Summarize(recorder, coverage, corpus);
        WriteArtifacts(recorder, options, coverage, engine.StillPresent);
    }

    private static UpgradeManifest? ReadManifest(CheckRecorder recorder, GameRuntime runtime)
    {
        Type? type = runtime.Mod.GetType(ManifestType, throwOnError: false);
        if (type is null)
        {
            recorder.Info(ManifestType, "Not in the build yet (contract K1): every legacy name that is not unchanged counts as undisposed.");
            return null;
        }

        MethodInfo? toJson = type.GetMethod("ToJson", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
        if (toJson is null || toJson.ReturnType != typeof(string))
        {
            recorder.Fail(ManifestType, "Must declare public static string ToJson() (contract K1).");
            return null;
        }

        try
        {
            string json = (string?)toJson.Invoke(null, null) ?? "";
            return UpgradeManifest.Parse(json);
        }
        catch (TargetInvocationException exception)
        {
            recorder.Fail(ManifestType + ".ToJson()", "Threw (it must be pure data, without Verse state): " + (exception.InnerException ?? exception).Message);
        }
        catch (JsonException exception)
        {
            recorder.Fail(ManifestType + ".ToJson()", "Returned malformed JSON: " + exception.Message);
        }

        return null;
    }

    // 存档戳永久冻结：类型名、标签与 forceSave（决定 §2；设计 §5.8）。
    private static void CheckStamp(CheckRecorder recorder, UpgradeManifest manifest, NewBuild build)
    {
        if (manifest.StampType != StampType || manifest.StampLabel != StampLabel)
        {
            recorder.Fail("frozen: schema stamp", $"The manifest names stamp {manifest.StampType}/{manifest.StampLabel}; the stamp is frozen as {StampType}/{StampLabel}.");
        }

        if (manifest.Schema < 1)
        {
            recorder.Fail("frozen: schema stamp", $"The manifest schema must be the current schema number (>= 1), not {manifest.Schema}.");
        }

        if (!build.Types.TryGetValue(StampType, out TypeDefinition? stamp) || !CecilNames.BaseTypeNames(stamp).Contains("Verse.GameComponent", StringComparer.Ordinal))
        {
            recorder.Fail("frozen: schema stamp", $"{StampType} must exist and derive from Verse.GameComponent.");
            return;
        }

        if (!build.LabelsOf(StampType, StampLabel).Any(fact => fact.ForceSave == "True"))
        {
            recorder.Fail("frozen: schema stamp", $"{StampType} must write '{StampLabel}' with forceSave: true (a missing node would read as the default).");
        }
    }

    // ratchet：未处置的必需行必须已列在其模块的 ratchet 文件中；列出但已处置的行报警告。
    private static void Ratchet(CheckRecorder recorder, ValidationOptions options, List<CoverageRow> coverage)
    {
        Dictionary<string, HashSet<string>> listed = ReadRatchets(options.BaselineDir);
        var listedKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string module, HashSet<string> keys) in listed)
        {
            foreach (string key in keys)
            {
                listedKeys.TryAdd(key, module);
            }
        }

        List<CoverageRow> undisposed = coverage.Where(row => row.IsUndisposed && row.IsRequired).ToList();
        var undisposedKeys = undisposed.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        int newDebt = 0;
        foreach (CoverageRow row in undisposed.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            if (options.SaveUpgradeStrict || options.RatchetReset || !listedKeys.ContainsKey(row.Key))
            {
                if (!options.RatchetReset)
                {
                    newDebt++;
                    recorder.Fail(row.Key, $"Undisposed legacy name ({row.Tiers}; module {row.Module}): {row.Detail}. Rename, consume (with a golden step) or drop it in the upgrade manifest"
                        + (options.SaveUpgradeStrict ? "." : $", or (integrator only) accept it into Baselines/{RatchetPrefix}{row.Module}.txt."));
                }
            }
        }

        List<string> stale = listedKeys.Keys.Where(key => !undisposedKeys.Contains(key)).Order(StringComparer.Ordinal).ToList();
        if (!options.SaveUpgradeStrict && !options.RatchetReset)
        {
            foreach (string key in stale)
            {
                recorder.Warn($"Baselines/{RatchetPrefix}{listedKeys[key]}.txt: {key}", "Disposed now (or no longer in the corpus): remove it from the ratchet (--update-baselines does).");
            }
        }

        if (options.RatchetReset)
        {
            WriteRatchets(options.BaselineDir, undisposed.GroupBy(r => r.Module).ToDictionary(g => g.Key, g => g.Select(r => r.Key).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal), listed.Keys);
            recorder.Info("ratchet", $"Reset: {undisposed.Count} undisposed required line(s) written to {undisposed.Select(r => r.Module).Distinct().Count()} ratchet file(s).");
        }
        else if (options.UpdateBaselines && stale.Count > 0)
        {
            var pruned = listed.ToDictionary(pair => pair.Key, pair => pair.Value.Where(undisposedKeys.Contains).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
            WriteRatchets(options.BaselineDir, pruned, listed.Keys);
            recorder.Info("ratchet", $"Removed {stale.Count} disposed line(s) from the ratchet files (--update-baselines).");
        }

        foreach (IGrouping<string, CoverageRow> module in undisposed.GroupBy(r => r.Module).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            recorder.Info("ratchet: " + module.Key, $"{module.Count()} undisposed required line(s); {listed.GetValueOrDefault(module.Key)?.Count ?? 0} accepted in Baselines/{RatchetPrefix}{module.Key}.txt.");
        }

        if (newDebt == 0 && !options.SaveUpgradeStrict)
        {
            recorder.Info("ratchet", "No undisposed required line outside the ratchet.");
        }
    }

    private static Dictionary<string, HashSet<string>> ReadRatchets(string baselineDir)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (!Directory.Exists(baselineDir))
        {
            return result;
        }

        foreach (string file in Directory.EnumerateFiles(baselineDir, RatchetPrefix + "*.txt").Order(StringComparer.Ordinal))
        {
            string module = Path.GetFileNameWithoutExtension(file)[RatchetPrefix.Length..];
            result[module] = LegacyCorpus.DataLines(file).ToHashSet(StringComparer.Ordinal);
        }

        return result;
    }

    private static void WriteRatchets(string baselineDir, Dictionary<string, HashSet<string>> byModule, IEnumerable<string> existingModules)
    {
        foreach (string module in byModule.Keys.Union(existingModules).Order(StringComparer.Ordinal))
        {
            string path = Path.Combine(baselineDir, RatchetPrefix + module + ".txt");
            HashSet<string> keys = byModule.GetValueOrDefault(module) ?? [];
            if (keys.Count == 0)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                continue;
            }

            var text = new StringBuilder();
            text.Append("# C.save-upgrade ratchet for module '").Append(module).Append("': legacy save names that are not disposed yet (one corpus key per line).\n");
            text.Append("# The count may only go down: a module's Wave S closes when this file is gone. Never add lines by hand.\n");
            foreach (string key in keys.Order(StringComparer.Ordinal))
            {
                text.Append(key).Append('\n');
            }

            File.WriteAllText(path, text.ToString(), Utf8);
        }
    }

    private static void Summarize(CheckRecorder recorder, List<CoverageRow> coverage, LegacyCorpus corpus)
    {
        foreach (IGrouping<string, CoverageRow> devOnly in coverage.Where(r => r.IsUndisposed && r.IsDevOnly).GroupBy(r => r.Module).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            recorder.Warn("dev tier: " + devOnly.Key, $"{devOnly.Count()} undisposed name(s) known only from dev builds (see {CoverageFile}).");
        }

        string counts = string.Join(", ", coverage.GroupBy(r => r.Disposition).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}"));
        recorder.Summary = $"{corpus.Builds.Count} legacy inputs, {corpus.RowCount} corpus lines; {counts}";
    }

    private static void WriteArtifacts(CheckRecorder recorder, ValidationOptions options, List<CoverageRow> coverage, IReadOnlySet<string> stillPresent)
    {
        if (options.ArtifactsDir is not { } directory)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var lines = new List<string> { "# key\ttiers\tdisposition\ttarget\tvia\tmodule\tdetail" };
        lines.AddRange(coverage.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => r.ToLine()));
        File.WriteAllText(Path.Combine(directory, CoverageFile), string.Join("\n", lines) + "\n", Utf8);

        // 只属于旧构建的名字：未保持不变、并且新构建中不再以同名存在的类型、Def、标签、枚举成员与存档字符串（形态变化的同名标签
        // 不进集合：重存扫描会把它们误判为残留）。
        var tokens = new SortedSet<string>(StringComparer.Ordinal);
        foreach (CoverageRow row in coverage.Where(r => r.Disposition is not ("unchanged" or "n/a") && !stillPresent.Contains(r.Key)))
        {
            string[] parts = row.Key.Split('|');
            string? token = parts[0] switch
            {
                LegacyCorpus.TypesFile => "type\t" + parts[1],
                LegacyCorpus.DefsFile => "def\t" + parts[1] + "/" + parts[2],
                LegacyCorpus.LabelsFile => "label\t" + parts[1] + "/" + parts[2],
                LegacyCorpus.EnumsFile => "enum\t" + parts[1] + "." + parts[2],
                LegacyCorpus.SavedValuesFile => "value\t" + parts[1] + "/" + parts[2],
                LegacyCorpus.ObservedFile => parts[1] switch
                {
                    "type" or "type?" => "type\t" + parts[2],
                    "label" => "label\t" + parts[3] + "/" + parts[2],
                    _ => "def\t*/" + parts[2],
                },
                _ => null,
            };
            if (token is not null)
            {
                tokens.Add(token);
            }
        }

        File.WriteAllText(Path.Combine(directory, TokenSetFile), "# kind\tname: names only legacy builds wrote (not unchanged in the current build)\n" + string.Join("\n", tokens) + "\n", Utf8);
        recorder.Info("artifacts", $"{CoverageFile} ({coverage.Count} lines) and {TokenSetFile} ({tokens.Count} tokens) written to {directory}.");
    }

    /// <summary>当前内容中为各类字段写的 XML 值（标签求值用，例如 CompProperties 的 saveKey）。</summary>
    private sealed class ContentFieldValues : IFieldValueSource
    {
        private readonly Dictionary<(string Type, string Field), HashSet<string>> values = [];

        public ContentFieldValues(ContentModel content)
        {
            foreach (XmlElement element in content.Corpus.Root.GetElementsByTagName("*").OfType<XmlElement>())
            {
                string cls = element.GetAttribute("Class");
                if (cls.Length == 0)
                {
                    continue;
                }

                foreach (XmlElement child in element.ChildNodes.OfType<XmlElement>().Where(c => !c.ChildNodes.OfType<XmlElement>().Any()))
                {
                    if (!values.TryGetValue((cls, child.Name), out HashSet<string>? set))
                    {
                        values[(cls, child.Name)] = set = new HashSet<string>(StringComparer.Ordinal);
                    }

                    set.Add(child.InnerText.Trim());
                }
            }
        }

        public IEnumerable<string> ValuesOf(string typeName, string field)
            => values.TryGetValue((typeName, field), out HashSet<string>? set) ? set : [];
    }
}
