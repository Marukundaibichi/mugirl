using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Mugirl.GameValidation.Core;

namespace Mugirl.GameValidation.Suites;

/// <summary>
/// 存档升级套件（critique-validation §4.3）：从主菜单读入夹具，核对统一升级器的诊断、备份、事实、残留旧名与差异，
/// 再在新进程中读回重存档，确认升级器保持不动、往返无差异。由 <c>tools/Test-InGame.ps1 -Fixture &lt;id|all|control&gt;</c> 启动。
/// </summary>
/// <remarks>
/// <para>
/// 升级器的诊断（K2：<c>Mugirl.Core.GameComponent_Mugirl</c> 的 SaveSchema/SourceSchema/LastUpgradeReportJson、
/// <c>Mugirl.SaveUpgrade.SaveUpgradeDiagnostics.LastReportJson</c>）与升级清单（K1：<c>SaveUpgradeManifest.ToJson()</c>）
/// 都按名字反射读取：本套件不随升级器的源码变化而需要重新编译，升级器尚未实现时各步骤给出明确的失败而不是编译错误。
/// </para>
/// <para>
/// 旧档夹具（P、E、R、X-*）第 1 次启动：load（任何错误都失败；印记、诊断、备份；与离线运行的每步动作数一致）、definjected、
/// resave（立即重存档，读档后未经过 Tick）、facts（从原始档逐值复现夹具清单）、tokens（类型明确的位置不再有旧名）、
/// diff（实际纯 DOM 升级产物加精确读取轨迹与重存档逐字段比较）、ticks（2500 Tick，播种的雪牛娘没有消失）、variants（X-settings、X-alias）。
/// </para>
/// <para>
/// 对照组（C：被测构建本次运行写出的存档）第 1 次启动与任何夹具的第 2 次启动（新进程读上一次的重存档）：升级器不动（NotLegacy、0 个动作、
/// 不做备份），diff 只接受有类型和精确输入/输出证据的解释；第 2 次启动另做 X-newer（印记为 Current+1 的副本：不改写、恰好一条警告、再存档保留该印记）。
/// </para>
/// <para>facts 保存原始证据，diff 验证实际 Scribe 没有漏掉升级结果；升级算法本身另由黄金夹具与行为差分验证。</para>
/// </remarks>
internal static partial class UpgradeSuite
{
    public const string Name = SuiteCatalog.UpgradeSuiteName;

    /// <summary>升级器的公开诊断与清单（K1/K2 契约）。</summary>
    public const string HubTypeName = "Mugirl.Core.GameComponent_Mugirl";
    public const string DiagnosticsTypeName = "Mugirl.SaveUpgrade.SaveUpgradeDiagnostics";
    public const string ManifestTypeName = "Mugirl.SaveUpgrade.SaveUpgradeManifest";
    public const string DefaultStampLabel = "mugirlSaveSchema";

    /// <summary>旧档第一次升级前的备份（decisions §5：<c>&lt;存档&gt;.rws.mugirl-schema0.bak</c>，从不覆盖）。</summary>
    public const string BackupSuffix = ".mugirl-schema0.bak";

    private const int TicksAfterUpgrade = 2500;
    private const int MaxListed = 30;

    public static ValidationSuite Create()
    {
        var state = new UpgradeState();
        return new ValidationSuite(
            Name,
            "load a save fixture through the unified save upgrader (boot 1) and its re-save in a fresh process (boot 2): diagnostics, stamp, backup, facts, "
                + "residual legacy names, canonical save diff, ticks, round trip, newer-schema save",
            SuiteNeeds.MugirlMod,
            [
                new ValidationStep("load", ctx => Load(ctx, state)),
                new ValidationStep("definjected", ctx => DefInjected(ctx)),
                new ValidationStep("resave", ctx => Resave(ctx, state), TimeoutSeconds: 300),
                new ValidationStep("facts", ctx => Facts(ctx, state), TimeoutSeconds: 300),
                new ValidationStep("tokens", ctx => Tokens(ctx, state), TimeoutSeconds: 300),
                new ValidationStep("diff", ctx => Diff(ctx, state), TimeoutSeconds: 300),
                new ValidationStep("ticks", ctx => Ticks(ctx, state), TimeoutSeconds: 600),
                new ValidationStep("variants", ctx => Variants(ctx, state), TimeoutSeconds: 600),
            ],
            inDefaultRun: false)
        {
            LogPolicy = SuiteLogPolicy.AnyError,
            InAllRun = false,
        };
    }

    private static IEnumerable<Wait> Load(ValidationContext ctx, UpgradeState state)
    {
        ValidationReport report = ctx.Report;
        MugirlTarget target = ctx.RequireTarget();
        if (!report.Check(ctx.Options.Entry == EntryMode.LoadSave && ctx.Options.EntrySave is not null, "the run loaded a save from the main menu",
            () => "the upgrade suite needs entry load:<save>; start it with tools/Test-InGame.ps1 -Fixture"))
        {
            yield break;
        }

        state.EntryPath = GenFilePaths.FilePathForSavedGame(ctx.Options.EntrySave!);
        state.Fixture = ctx.Options.FixtureFor(ctx.Options.EntrySave);
        state.Boot = ctx.Options.Boot;
        state.Kind = state.Fixture?.Kind ?? FixtureKind.Published;
        state.Upgrading = state.Boot == 1 && FixtureKinds.IsLegacy(state.Kind);
        report.Info($"boot {state.Boot}: {ctx.Options.EntrySave} (fixture {state.Fixture?.Id ?? "-"}, kind {FixtureKinds.Text(state.Kind)}; "
            + $"{(state.Upgrading ? "a legacy save: the upgrader must run" : "written by the build under test: the upgrader must stay inert")})");
        ReportConverterPath(ctx, state);
        if (state.Fixture?.ManifestPath is { } manifestPath)
        {
            state.FixtureManifest = ArtifactJson.TryRead(manifestPath, out string problem) as JsonObject;
            report.Check(state.FixtureManifest is not null, "the fixture manifest is readable", () => problem);
        }
        else
        {
            report.Fail("the fixture manifest is readable", "the run options name no fixture manifest");
        }

        if (state.Fixture?.VariantManifestPath is { } variantPath)
        {
            state.VariantManifest = ArtifactJson.TryRead(variantPath, out string variantProblem) as JsonObject;
            report.Check(state.VariantManifest is not null, "the variant fixture manifest is readable", () => variantProblem);
        }

        state.LegacyDefs = Strings(state.FixtureManifest?["modDefs"]);
        state.LegacyThingDefs = Strings(state.FixtureManifest?["modThingDefs"]);
        state.RestraintDefaults = RestraintDefaults(state.FixtureManifest?["restraintDefaults"]);
        List<Def> currentDefs = target.AllDefs();
        state.CurrentDefs = [.. currentDefs.Select(d => d.defName)];
        state.CurrentThingDefs = [.. currentDefs.OfType<ThingDef>().Select(d => d.defName)];

        // 升级清单（K1）：翻译名字、解释差异、推导旧名单；缺失时以空映射继续，后面的比较照常执行并报告差异。
        state.ManifestJson = ReadUpgradeManifest(target, out string manifestProblem);
        state.Map = state.ManifestJson is null ? SaveNameMap.Empty : SaveNameMap.Parse(state.ManifestJson);
        report.Check(manifestProblem.Length == 0, "the upgrade manifest (K1) is available", () => manifestProblem);
        report.Info("upgrade manifest: " + state.Map);

        // 旧版语料：组件标签与 owner 选择器的旧 Def→组件关系。
        LegacyCorpus? corpus = LegacyCorpus.TryLoad(ctx.Options.LegacyCorpusDir, out string corpusProblem);
        report.Check(corpus is not null || !state.Upgrading, "the legacy corpus is available", () => corpusProblem);
        var roots = new CurrentScribeRoots(target.Assembly!);
        roots.Discover(DefDatabase<ThingDef>.AllDefsListForReading.SelectMany(d => d.comps)
            .Select(p => p.compClass).Where(t => t?.Assembly == target.Assembly).OfType<Type>());
        ReportList(report, "every current component Scribe root is identified", roots.Problems);
        state.Catalog = new SaveOwnerCatalog(state.Map, corpus ?? LegacyCorpus.Empty, CurrentComps(), roots.Labels);
        state.SerializationPawnDefaults = SerializationPawnDefaults.Read(DefDatabase<ThingDef>.AllDefsListForReading, target.Assembly!);
        if (ctx.ArtifactPath("comparison-scope.json") is { } scopePath)
        {
            ArtifactJson.Write(scopePath, new JsonObject
            {
                ["productionMvid"] = target.Assembly!.ManifestModule.ModuleVersionId.ToString(),
                ["modDefs"] = new JsonArray([.. state.CurrentDefs.Select(d => (JsonNode?)JsonValue.Create(d))]),
                ["modThingDefs"] = new JsonArray([.. state.CurrentThingDefs.Select(d => (JsonNode?)JsonValue.Create(d))]),
                ["componentRoots"] = new JsonObject([.. roots.Labels.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new KeyValuePair<string, JsonNode?>(p.Key,
                    new JsonArray([.. p.Value.Order(StringComparer.Ordinal).Select(l => (JsonNode?)JsonValue.Create(l))])))]),
                ["labelsByThingDef"] = new JsonObject([.. state.Catalog.SnapshotCompLabels().Select(p => new KeyValuePair<string, JsonNode?>(p.Key,
                    new JsonArray([.. p.Value.Select(l => (JsonNode?)JsonValue.Create(l))])))]),
                ["unresolvedRoots"] = new JsonArray([.. roots.Problems.Select(p => (JsonNode?)JsonValue.Create(p))]),
                ["serializationPawnDefaults"] = SerializationPawnDefaults.ToJson(state.SerializationPawnDefaults),
            });
        }
        state.ReadTraceJson = CanonicalSavePreview.ReadTrace(target.Assembly!);
        if (state.ReadTraceJson is { } readTrace && ctx.ArtifactPath("scribe-read-trace.json") is { } readTracePath)
        {
            File.WriteAllText(readTracePath, readTrace);
        }
        report.Check(state.ReadTraceJson is not null, "the precise Scribe read trace is available", () => "the runtime trace was not captured for this load");
        state.MissingComponents = MissingComponentProjection.Capture(target.Assembly!);
        report.Info($"legacy corpus: {corpus?.DefCompRows ?? 0} def-comp row(s), {corpus?.LabelRows ?? 0} label row(s); "
            + $"{state.Catalog.ThingDefsWithModCompLabels} ThingDef(s) with mod comp labels");

        // 日志记号：只用旧构建独有的名字（当前名字由 "mugirl" 归因覆盖；把当前的短 defName 当作记号会误中普通单词）。
        state.AllCurrentDefNames = AllDefNames();
        ctx.LogPolicy.AddModTokens(state.Map.LegacyOnlyTypes());
        ctx.LogPolicy.AddModTokens(state.Map.LegacyOnlyDefs(state.AllCurrentDefNames));
        ctx.LogPolicy.AddModTokens(state.LegacyDefs.Where(d => !state.AllCurrentDefNames.Contains(d)));
        if (ctx.Options.TokenSetPath is { } tokenPath)
        {
            state.TokenSet = ReadTokenSet(tokenPath, report);
            ctx.LogPolicy.AddModTokens(state.TokenSet);
        }

        report.Info($"{ctx.LogPolicy.ModTokenCount} legacy-only token(s) attribute log entries to the mod");
        foreach (string rejected in ctx.LogPolicy.RejectedForeignTokens)
        {
            report.Info($"foreign token {rejected} rejected: it names Mugirl content");
        }

        // 升级器的诊断（K2）。
        object? hub = Current.Game.components.FirstOrDefault(c => c.GetType().FullName == HubTypeName);
        int? saveSchema = ReadInt(hub, "SaveSchema");
        int? sourceSchema = ReadInt(hub, "SourceSchema");
        string? hubReport = ReadString(hub, "LastUpgradeReportJson");
        string? lastReport = ReadStaticString(target, DiagnosticsTypeName, "LastReportJson");
        JsonObject? diagnostics = lastReport is null ? null : JsonNode.Parse(lastReport) as JsonObject;
        state.Diagnostics = diagnostics;
        state.Session = (int?)diagnostics?["session"] ?? -1;
        report.Check(hub is not null, "the lifecycle hub GameComponent exists", () => HubTypeName + " is not among the game's components");
        report.Check(diagnostics is not null, "the upgrader reports the last load", () => DiagnosticsTypeName + ".LastReportJson is " + (lastReport is null ? "unavailable" : "not a JSON object"));
        string? upgradeState = (string?)diagnostics?["state"];
        long actions = TotalActions(diagnostics);
        string? backup = (string?)diagnostics?["backup"];
        report.Info($"diagnostics: state {upgradeState ?? "-"}, {actions} action(s), {Steps(diagnostics).Count} step(s), total {(long?)diagnostics?["totalMs"] ?? -1} ms, "
            + $"backup {backup ?? "-"}; hub SaveSchema {Text(saveSchema)}, SourceSchema {Text(sourceSchema)}");
        foreach ((string id, string phase, long count, long ms) in Steps(diagnostics).Take(MaxListed))
        {
            report.Info($"step {id} ({phase}): {count} action(s), {ms} ms");
        }

        report.Check(hubReport is not null && lastReport is not null && hubReport == lastReport, "the hub holds the same report as the diagnostics",
            () => "hub report " + (hubReport is null ? "missing" : "differs"));
        report.Check(saveSchema is not null && saveSchema == state.Map.Schema, "the stamp to write equals the current schema",
            () => $"SaveSchema {Text(saveSchema)}, manifest schema {state.Map.Schema}");
        string backupPath = state.EntryPath + BackupSuffix;
        if (state.Upgrading)
        {
            report.Check(upgradeState == "Upgraded", "the legacy fixture was upgraded", () => $"state {upgradeState ?? "-"}, failed step {(string?)diagnostics?["failedStep"] ?? "-"}");
            report.Check(sourceSchema == 0, "the fixture is detected as schema 0 (legacy)", () => "SourceSchema " + Text(sourceSchema));
            CompareWithOffline(ctx, diagnostics);

            // 备份（decisions §5）：第一次升级前把原文件复制一份，内容与夹具相同，诊断报告它的路径。
            string? expectedSha = (string?)state.FixtureManifest?["save"]?["sha256"];
            bool exists = File.Exists(backupPath);
            report.Check(exists, "the original save is backed up beside it", () => "no " + backupPath);
            if (exists)
            {
                report.Check(expectedSha is null || string.Equals(Sha256(backupPath), expectedSha, StringComparison.OrdinalIgnoreCase),
                    "the backup equals the fixture", () => $"SHA-256 {Sha256(backupPath)}, fixture {expectedSha}");
            }

            report.Check(backup is not null && SamePath(backup, backupPath), "the diagnostics name the backup", () => $"backup {backup ?? "-"}, expected {backupPath}");
        }
        else
        {
            report.Check(upgradeState == "NotLegacy", "the upgrader stays inert on a save written by the build under test", () => "state " + (upgradeState ?? "-"));
            report.Check(actions == 0, "the upgrader made no edit", () => $"{actions} action(s)");
            report.Check(sourceSchema is not null && sourceSchema == saveSchema, "the loaded save carries the current stamp",
                () => $"SourceSchema {Text(sourceSchema)}, SaveSchema {Text(saveSchema)}");
            string? stamp = StampIn(state.EntryDocument, state.Map);
            report.Check(stamp == state.Map.Schema.ToString(CultureInfo.InvariantCulture), "the loaded file holds the stamp " + StampLabel(state.Map),
                () => $"stamp {stamp ?? "missing"}, schema {state.Map.Schema}");
            report.Check(BackupsOf(state.EntryPath).Count == 0 && backup is null, "no backup is made of a save that needs no upgrade",
                () => $"backups {string.Join(", ", BackupsOf(state.EntryPath))}; diagnostics backup {backup ?? "-"}");
        }
        CheckCompletionLetter(report, state);
    }

    private static void CheckCompletionLetter(ValidationReport report, UpgradeState state)
    {
        string title = "Mugirl.SaveUpgrade.CompletedTitle".Translate().ToString();
        XElement game = SaveXml.Game(state.EntryDocument)!;
        HashSet<string> activeIds = [.. game.Element("letterStack")?.Element("letters")?.Elements("li").Select(e => e.Value) ?? []];
        HashSet<int> previous = [.. game.Descendants("li").Where(e => e.Element("label")?.Value == title
            && e.Element("def")?.Value == LetterDefOf.PositiveEvent.defName && activeIds.Contains("Letter_" + e.Element("ID")?.Value))
            .Select(e => int.Parse(e.Element("ID")!.Value, CultureInfo.InvariantCulture))];
        Letter[] current = [.. Find.LetterStack.LettersListForReading.Where(l => l.def == LetterDefOf.PositiveEvent && l.Label.ToString() == title)];
        Letter[] added = [.. current.Where(l => !previous.Contains(l.ID))];
        int expected = state.Upgrading ? 1 : 0;
        report.Check(added.Length == expected && current.Length == previous.Count + expected,
            state.Upgrading ? "one success letter announces the completed upgrade" : "reloading the current schema sends no new upgrade letter",
            () => $"source {previous.Count}, current {current.Length}, new {added.Length}, expected new {expected}");
        if (state.Upgrading && added.Length == 1) { state.CompletionLetterId = added[0].ID; }
    }

    /// <summary>
    /// DefInjected：被测 Mod 当前语言的每个 DefInjected 文件中的每个键都已注入（或是占位），并且没有指向这些文件的加载错误。
    /// 键按原版读取规则求出（元素名中的 [ ] 换成点，defName 经 BackCompatibleDefName），在该 Def 类型的注入包中查找。
    /// </summary>
    private static IEnumerable<Wait> DefInjected(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        MugirlTarget target = ctx.RequireTarget();
        LoadedLanguage? language = LanguageDatabase.activeLanguage;
        if (language is null)
        {
            report.Fail("definjected", "no active language");
            yield break;
        }

        var problems = new List<string>();
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int keys = 0;
        int overridden = 0;
        foreach (Tuple<RimWorld.IO.VirtualDirectory, ModContentPack, string> directory in language.AllDirectories.Where(d => d.Item2 == target.Mod))
        {
            RimWorld.IO.VirtualDirectory injected = directory.Item1.GetDirectory("DefInjected");
            if (!injected.Exists)
            {
                continue;
            }

            foreach (RimWorld.IO.VirtualDirectory typeFolder in injected.GetDirectories("*", SearchOption.TopDirectoryOnly))
            {
                Type? defType = GenTypes.GetTypeInAnyAssembly(typeFolder.Name)
                    ?? (typeFolder.Name.Length > 3 ? GenTypes.GetTypeInAnyAssembly(typeFolder.Name[..^1]) : null);
                DefInjectionPackage? package = defType is null ? null : language.defInjections.FirstOrDefault(p => p.defType == defType);
                foreach (RimWorld.IO.VirtualFile file in typeFolder.GetFiles("*.xml", SearchOption.AllDirectories))
                {
                    // 同一 Mod 中较高优先级的加载目录已有同一相对路径的文件时，原版跳过这个（TryRegisterFileIfNew）。
                    string relative = typeFolder.Name + "/" + file.Name;
                    if (!seen.Add(relative))
                    {
                        continue;
                    }

                    fileNames.Add(file.Name);
                    if (package is null)
                    {
                        problems.Add($"{relative}: no def type named {typeFolder.Name}");
                        continue;
                    }

                    foreach (string key in InjectionKeys(file, defType!, problems))
                    {
                        keys++;
                        if (!package.injections.TryGetValue(key, out DefInjectionPackage.DefInjection? injection))
                        {
                            problems.Add($"{relative}: {key} was not loaded");
                        }
                        else if (!string.Equals(injection.fileSource, file.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            overridden++;
                        }
                        else if (!injection.injected && !injection.isPlaceholder)
                        {
                            problems.Add($"{relative}: {key} is not injected");
                        }
                    }
                }
            }
        }

        // 原版的加载错误只带文件名（DefInjectionPackage 写 file.Name）：按本 Mod 的文件名归因。
        foreach (DefInjectionPackage package in language.defInjections)
        {
            problems.AddRange(package.loadErrors.Where(e => fileNames.Any(name => e.Contains("(" + name + ")", StringComparison.OrdinalIgnoreCase)
                || e.Contains("file " + name, StringComparison.OrdinalIgnoreCase))).Select(e => "load error: " + e));
        }

        report.Info($"{keys} DefInjected key(s) in {fileNames.Count} file(s) of {language.folderName} come from the mod; {overridden} overridden by a later mod");
        report.Check(keys > 0, "the mod's DefInjected files were found", () => $"no DefInjected key of the mod in {language.folderName}");
        ReportList(report, "every DefInjected entry of the mod is injected", problems);
    }

    private static IEnumerable<Wait> Resave(ValidationContext ctx, UpgradeState state)
    {
        ValidationReport report = ctx.Report;
        if (state.EntryPath is null)
        {
            report.Skip("resave", "the load step found no entry save");
            yield break;
        }

        string? loadedTick = SaveXml.Game(state.EntryDocument)?.Element("tickManager")?.Element("ticksGame")?.Value.Trim();
        report.Check(loadedTick == Find.TickManager.TicksGame.ToString(CultureInfo.InvariantCulture), "no tick passed since loading",
            () => $"loaded tick {loadedTick}, now {Find.TickManager.TicksGame}");
        var save = new SaveWait(ctx.Options.ResaveName ?? ValidationContext.SaveName($"upgrade-boot{state.Boot}"));
        yield return save;
        if (report.Check(File.Exists(save.FilePath) && new FileInfo(save.FilePath).Length > 0, "the upgraded game is saved", () => "no save at " + save.FilePath))
        {
            state.ResavePath = save.FilePath;
            state.VanillaReadJson = VanillaReadObservation.SnapshotJson();
            state.PriceReadJson = CorporatePriceObservation.SnapshotJson();
            state.QualityReadJson = UnconsumedQualityObservation.SnapshotJson();
            if (ctx.ArtifactPath("quality-read-observations.json") is { } qualityPath)
            {
                File.WriteAllText(qualityPath, state.QualityReadJson);
            }
            if (ctx.ArtifactPath("corporate-price-observations.json") is { } pricePath)
            {
                File.WriteAllText(pricePath, state.PriceReadJson);
            }
            if (ctx.ArtifactPath("vanilla-read-observations.json") is { } vanillaPath)
            {
                File.WriteAllText(vanillaPath, state.VanillaReadJson);
            }
            report.Info($"re-saved {save.FilePath} ({new FileInfo(save.FilePath).Length / 1024} KiB)");
            string? stamp = StampIn(state.ResaveDocument!, state.Map);
            report.Check(stamp == state.Map.Schema.ToString(CultureInfo.InvariantCulture), "the re-save carries the stamp " + StampLabel(state.Map),
                () => $"stamp {stamp ?? "missing"}, schema {state.Map.Schema}");
            if (state.CompletionLetterId is { } letterId)
            {
                XElement[] letters = [.. SaveXml.Game(state.ResaveDocument!)!.Descendants("li").Where(e =>
                    e.Element("ID")?.Value == letterId.ToString(CultureInfo.InvariantCulture)
                    && e.Element("def")?.Value == LetterDefOf.PositiveEvent.defName
                    && e.Element("label")?.Value == "Mugirl.SaveUpgrade.CompletedTitle".Translate().ToString())];
                string backup = (string?)state.Diagnostics?["backup"] ?? "";
                report.Check(letters.Length == 1 && backup.Length > 0 && letters[0].Element("text")?.Value.Contains(backup, StringComparison.Ordinal) == true,
                    "the saved success letter names the original backup", () => $"matching letters {letters.Length}, backup {backup}");
            }
        }
    }

    private static IEnumerable<Wait> Facts(ValidationContext ctx, UpgradeState state)
    {
        ValidationReport report = ctx.Report;
        if (state.Boot != 1)
        {
            report.Skip("facts", "boot 2 compares the two re-saves in the diff step");
            yield break;
        }

        if (state.ResaveDocument is not { } resave || state.FixtureManifest?["facts"] is not JsonObject manifestFacts)
        {
            report.Skip("facts", state.ResavePath is null ? "no re-save" : "the fixture manifest has no facts");
            yield break;
        }

        SortedDictionary<string, string> manifest = ArtifactJson.StringMap(manifestFacts);
        SaveFactQueries queries = SaveFactQueries.Legacy(state.LegacyDefs, state.LegacyThingDefs, state.RestraintDefaults);
        if ((string?)state.FixtureManifest["factsFormat"] == "current-v1")
        {
            queries = SaveFactQueries.Current(state.LegacyDefs, state.LegacyThingDefs, state.RestraintDefaults);
        }
        SaveFacts sourceFacts = SaveFacts.Read(state.EntryDocument, queries);
        var mismatches = new List<string>();
        foreach ((string key, string value) in manifest)
        {
            if (!sourceFacts.Values.TryGetValue(key, out string? found) || !SaveXml.SameValue(value, found))
            {
                mismatches.Add($"{key}: manifest {value}, original archive {found ?? "missing"}");
            }
        }

        // 清单只证明原始证据可复现；结构转换后的保存义务由真实 DOM 产物的完整深比较承担。
        // 不能把 Consumed 事实删掉后声称它们已经在重存档保留。
        report.Info($"{manifest.Count} original fixture fact(s) checked against the immutable input; deep canonical comparison verifies persistence after conversion");
        ReportList(report, "the fixture facts reproduce the original input", mismatches);
    }

    private static IEnumerable<Wait> Tokens(ValidationContext ctx, UpgradeState state)
    {
        ValidationReport report = ctx.Report;
        if (state.ResaveDocument is not { } resave || SaveXml.Game(resave) is not { } game)
        {
            report.Skip("tokens", "no re-save");
            yield break;
        }

        var scan = new TokenScan(state);
        if (scan.IsEmpty)
        {
            report.Skip("legacy names", "no token set and no legacy-only name in the upgrade manifest");
            yield break;
        }

        scan.Run(game);
        report.Info($"searched the re-save for {scan.Describe()}");
        ReportList(report, "no legacy name is left in mod-owned positions", scan.Hits);
    }

    private static IEnumerable<Wait> Diff(ValidationContext ctx, UpgradeState state)
    {
        ValidationReport report = ctx.Report;
        MugirlTarget target = ctx.RequireTarget();
        if (state.ResaveDocument is not { } after || state.EntryPath is null)
        {
            report.Skip("diff", "no re-save");
            yield break;
        }

        XDocument? before = null;
        try
        {
            before = ExpectedDocument(ctx, state, target);
        }
        catch (Exception ex)
        {
            report.Fail("canonical save expectation", ex.ToString());
        }
        if (before is null)
        {
            yield break;
        }

        var defs = new HashSet<string>(state.CurrentDefs, StringComparer.Ordinal);
        defs.UnionWith(state.LegacyDefs);
        var thingDefs = new HashSet<string>(state.CurrentThingDefs, StringComparer.Ordinal);
        thingDefs.UnionWith(state.LegacyThingDefs);
        Dictionary<string, int> duplicateColors = DefDatabase<ThingDef>.AllDefsListForReading
            .Select(d => (d.defName, Count: d.comps.Count(c => c.compClass == typeof(CompColorable))))
            .Where(d => d.Count > 1).ToDictionary(d => d.defName, d => d.Count, StringComparer.Ordinal);
        // 这些初始化路径已在两版引擎逐方法核实；未知程序集不能静默放行。
        bool collectionSchemaVerified = ReviewedEngine.IsReviewed(typeof(Scribe_Collections).Assembly.ManifestModule.ModuleVersionId);
        HashSet<string> emptyAbilityApparelDefs = collectionSchemaVerified
            ? [.. DefDatabase<ThingDef>.AllDefsListForReading.Where(d => typeof(Apparel).IsAssignableFrom(d.thingClass) && d.apparel?.abilities is { Count: 0 }).Select(d => d.defName)]
            : [];
        HashSet<string> explosiveReferenceListDefs = collectionSchemaVerified
            ? [.. DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.comps.Any(c => c.compClass == typeof(CompExplosive))).Select(d => d.defName)]
            : [];
        var defaultTargetVerbClasses = new HashSet<string>(StringComparer.Ordinal);
        if (collectionSchemaVerified)
        {
            foreach (Type type in typeof(Verb).Assembly.GetTypes().Where(t => typeof(Verb).IsAssignableFrom(t)
                && t.GetMethod(nameof(Verb.ExposeData), Type.EmptyTypes)?.DeclaringType == typeof(Verb)))
            {
                defaultTargetVerbClasses.Add(type.FullName!);
                if (GenTypes.AllTypes.Count(t => t.Name == type.Name) == 1)
                {
                    defaultTargetVerbClasses.Add(type.Name);
                }
            }
        }
        IReadOnlySet<string> duplicateForbidden = DuplicateForbiddableEvidence.Capture(Find.Maps.SelectMany(m => m.listerThings.AllThings), out JsonObject forbiddenEvidence);
        if (ctx.ArtifactPath("duplicate-forbiddable-evidence.json") is { } forbiddenPath) { ArtifactJson.Write(forbiddenPath, forbiddenEvidence); }
        var scope = new SaveDiffScope(defs, thingDefs, state.Catalog!.IsModCompLabel, new HashSet<string>(StringComparer.Ordinal), duplicateColors,
            emptyAbilityApparelDefs, explosiveReferenceListDefs, defaultTargetVerbClasses, duplicateForbidden);
        var diff = new SaveDiff(scope);
        List<SaveDifference> differences = diff.Compare(before, after);
        if (state.Upgrading && ctx.ArtifactPath("source-to-resave-diff.json") is { } originalDiffPath)
        {
            List<SaveDifference> originalDifferences = new SaveDiff(scope).Compare(state.EntryDocument, after);
            ArtifactJson.Write(originalDiffPath, new JsonObject
            {
                ["comparisonBase"] = "original immutable input; no mapping or normalization",
                ["count"] = originalDifferences.Count,
                ["differences"] = new JsonArray([.. originalDifferences.Select(d => (JsonNode)new JsonObject
                {
                    ["kind"] = d.Kind, ["item"] = d.Item, ["path"] = d.Path,
                    ["before"] = d.BeforeElement?.ToString(SaveOptions.DisableFormatting) ?? d.Before,
                    ["after"] = d.AfterElement?.ToString(SaveOptions.DisableFormatting) ?? d.After,
                })]),
            });
        }
        var explainedNormalizations = differences.Where(d => diff.ExplainColorableNormalization(d) is not null).ToList();
        var forbiddenNormalizations = differences.Where(d => diff.ExplainForbiddableNormalization(d) is not null).ToList();
        var collectionInitializations = differences.Where(d => diff.ExplainCollectionInitialization(d) is not null).ToList();
        var verbTargetDefaults = differences.Where(d => diff.ExplainVerbTargetDefault(d) is not null).ToList();
        if (ctx.ArtifactPath("save-diff.json") is { } diffPath)
        {
            ArtifactJson.Write(diffPath, new JsonObject
            {
                ["comparisonBase"] = "canonical production DOM plus exact runtime read events",
                ["rawCount"] = differences.Count,
                ["colorableNormalizations"] = explainedNormalizations.Count,
                ["forbiddableNormalizations"] = forbiddenNormalizations.Count,
                ["collectionInitializations"] = collectionInitializations.Count,
                ["verbTargetDefaults"] = verbTargetDefaults.Count,
                ["normalizationEvidence"] = new JsonObject
                {
                    ["gameAssemblyMvid"] = typeof(Scribe_Collections).Assembly.ManifestModule.ModuleVersionId.ToString(),
                    ["collectionSchemaVerified"] = collectionSchemaVerified,
                    ["emptyAbilityApparelDefs"] = new JsonArray([.. emptyAbilityApparelDefs.Order(StringComparer.Ordinal).Select(d => (JsonNode?)JsonValue.Create(d))]),
                    ["explosiveReferenceListDefs"] = new JsonArray([.. explosiveReferenceListDefs.Order(StringComparer.Ordinal).Select(d => (JsonNode?)JsonValue.Create(d))]),
                    ["defaultTargetVerbClasses"] = new JsonArray([.. defaultTargetVerbClasses.Order(StringComparer.Ordinal).Select(d => (JsonNode?)JsonValue.Create(d))]),
                },
                ["differences"] = new JsonArray([.. differences.Select(d => (JsonNode)new JsonObject
                {
                    ["kind"] = d.Kind, ["item"] = d.Item, ["path"] = d.Path,
                    ["before"] = d.BeforeElement?.ToString(SaveOptions.DisableFormatting) ?? d.Before,
                    ["after"] = d.AfterElement?.ToString(SaveOptions.DisableFormatting) ?? d.After,
                    ["explanation"] = diff.ExplainColorableNormalization(d) ?? diff.ExplainForbiddableNormalization(d) ?? diff.ExplainCollectionInitialization(d) ?? diff.ExplainVerbTargetDefault(d),
                })]),
            });
            report.Info("complete raw save differences and normalization explanations: " + diffPath);
        }
        report.Info($"compared items: {diff.DescribeCounts()}; {differences.Count} difference(s); {diff.LoadResets.Count} vanilla load reset(s)");
        foreach (string reset in diff.LoadResets.Take(10))
        {
            report.Info("load reset: " + reset);
        }

        foreach (string change in diff.JobChanges(before, after).Take(MaxListed))
        {
            report.Info("job change: " + change);
        }

        // IsNull 与空文本不是一般等价：只允许 Def 类型和引擎方法都已核实的两个单向集合初始化。
        List<SaveDifference> nullness = [.. differences.Where(d => d.Kind == "nullness")];
        report.Info($"{nullness.Count} IsNull difference(s), {collectionInitializations.Count} verified typed collection initialization(s); game schema verified: {collectionSchemaVerified}");
        report.Info($"{explainedNormalizations.Count} duplicate CompColorable normalization(s), verified against runtime Def components and unchanged primary values");
        report.Info($"{forbiddenNormalizations.Count} duplicate CompForbiddable normalization(s), verified against exact runtime objects, readers and unchanged True values");
        report.Info($"{verbTargetDefaults.Count} vanilla Verb Invalid target default(s), verified against the exact ExposeData implementation");
        foreach (SaveDifference difference in nullness.Take(10))
        {
            report.Info("nullness: " + difference);
        }

        ReportList(report, state.Upgrading ? "actual Scribe output preserves the complete canonical upgrade result"
                : state.Boot == 1 ? "loading a save of the build under test changes nothing mod-owned" : "the round trip changes nothing mod-owned",
            [.. differences.Where(d => diff.ExplainCollectionInitialization(d) is null && diff.ExplainColorableNormalization(d) is null
                && diff.ExplainVerbTargetDefault(d) is null && diff.ExplainForbiddableNormalization(d) is null).Select(d => d.ToString())]);
    }

    private static XDocument ExpectedDocument(ValidationContext ctx, UpgradeState state, MugirlTarget target)
    {
        XDocument expected;
        if (state.Upgrading)
        {
            CanonicalSavePreview preview = CanonicalSavePreview.Create(target.Assembly!, state.EntryDocument);
            expected = preview.Document;
            if (ctx.ArtifactPath("canonical-dom-report.json") is { } reportPath)
            {
                File.WriteAllText(reportPath, preview.ReportJson);
            }
        }
        else
        {
            expected = new XDocument(state.EntryDocument);
        }

        if (ctx.ArtifactPath("canonical-dom.rws") is { } domPath)
        {
            expected.Save(domPath);
        }

        string trace = state.ReadTraceJson ?? throw new InvalidOperationException("No precise Scribe read trace was captured for this load.");
        if (ctx.ArtifactPath("scribe-read-trace.json") is { } tracePath)
        {
            File.WriteAllText(tracePath, trace);
        }

        SaveActionParity.VerifyDictionaryActions(state.Diagnostics, trace);
        int edits = SaveReadTrace.Apply(expected, trace);
        ctx.Report.Info($"{edits} exact source values replayed from the actual Scribe read trace");
        int initialized = VanillaReadEvidence.Apply(expected, state.VanillaReadJson ?? throw new InvalidOperationException("No vanilla initialization observations were captured."));
        ctx.Report.Info($"{initialized} exact vanilla tracker initializations matched their source, original method and actual write");
        int completedPrices = CorporatePriceObservation.Apply(expected, state.PriceReadJson ?? throw new InvalidOperationException("No exact corporate pricing observations were captured."));
        ctx.Report.Info($"{completedPrices} exact corporate price completions matched natural inputs, identity and saving callbacks");
        int unusedQuality = UnconsumedQualityObservation.Apply(expected, state.QualityReadJson ?? throw new InvalidOperationException("No exact quality reader observations were captured."));
        ctx.Report.Info($"{unusedQuality} unused quality leaves matched the actual apparel readers, components and converter gate");
        if (state.Upgrading)
        {
            AddUpgradeRecord(expected, state);
        }

        if (ctx.ArtifactPath("canonical-runtime-read.rws") is { } readPath)
        {
            expected.Save(readPath);
        }
        MissingComponentProjection components = state.MissingComponents ?? throw new InvalidOperationException("No actual component-fill order was captured.");
        bool filled = false;
        try
        {
            components.Apply(expected, state.EntryDocument);
            filled = true;
        }
        finally
        {
            if (ctx.ArtifactPath("missing-component-projection.json") is { } componentPath)
            {
                ArtifactJson.Write(componentPath, new JsonObject { ["completed"] = filled, ["actions"] = components.Actions.DeepClone(),
                    ["readers"] = components.Readers.DeepClone(), ["orderEvidence"] = components.OrderEvidence.DeepClone() });
            }
        }
        if (ctx.ArtifactPath("canonical-component-defaults.rws") is { } componentDocument) { expected.Save(componentDocument); }
        var projection = new CurrentSerializationDefaults(target.Assembly!, state.SerializationPawnDefaults);
        bool projected = false;
        try
        {
            projection.Apply(expected, state.EntryDocument);
            projected = true;
            ctx.Report.Info($"{projection.Actions.Count} exact current serialization default form(s) projected from independent reader/configuration evidence");
        }
        finally
        {
            if (ctx.ArtifactPath("serialization-default-projection.json") is { } projectionPath)
            {
                ArtifactJson.Write(projectionPath, new JsonObject
                {
                    ["completed"] = projected, ["actions"] = projection.Actions.DeepClone(), ["readers"] = projection.Readers.DeepClone(),
                    ["configurationSource"] = "actual runtime DefDatabase CompProperties; no values read from the after document",
                    ["pawnDefs"] = SerializationPawnDefaults.ToJson(state.SerializationPawnDefaults),
                });
            }
        }

        if (ctx.ArtifactPath("canonical-expected.rws") is { } expectedPath)
        {
            expected.Save(expectedPath);
        }

        if (ctx.ArtifactPath("original-input.rws") is { } inputPath)
        {
            File.Copy(state.EntryPath!, inputPath, overwrite: true);
        }

        return expected;
    }

    // 这份诊断记录有固定的公开保存契约；仅补这四个精确字段，不放行整个 Hub 或任意新增容器。
    private static void AddUpgradeRecord(XDocument expected, UpgradeState state)
    {
        XElement components = SaveXml.Game(expected)?.Element("components")
            ?? throw new InvalidOperationException("Canonical save has no game components container.");
        List<XElement> hubs = [.. components.Elements("li").Where(e => SaveXml.ClassOf(e) == HubTypeName)];
        if (hubs.Count > 1)
        {
            throw new InvalidOperationException("Canonical save contains duplicate lifecycle hubs.");
        }

        XElement hub = hubs.FirstOrDefault() ?? new XElement("li", new XAttribute("Class", HubTypeName));
        if (hub.Parent is null)
        {
            components.Add(hub);
        }

        hub.SetElementValue(StampLabel(state.Map), state.Map.Schema);
        hub.Element("lastUpgrade")?.Remove();
        var record = new XElement("lastUpgrade");
        if (state.Map.Schema != 0)
        {
            record.Add(new XElement("toSchema", state.Map.Schema));
        }

        record.Add(new XElement("stepIds", Steps(state.Diagnostics).Where(s => s.Actions > 0).Select(s => new XElement("li", s.Id))));
        string? gameVersion = state.EntryDocument.Root?.Element("meta")?.Element("gameVersion")?.Value;
        if (gameVersion is not null)
        {
            record.Add(new XElement("gameVersion", gameVersion));
        }

        hub.Add(record);
    }

    private static IEnumerable<Wait> Ticks(ValidationContext ctx, UpgradeState state)
    {
        ValidationReport report = ctx.Report;
        if (state.Boot != 1)
        {
            report.Skip("ticks", "boot 2 does not tick");
            yield break;
        }

        List<int> seeded = [.. ArtifactJson.StringMap(state.FixtureManifest?["facts"])
            .Where(p => p.Key.StartsWith("pawn/", StringComparison.Ordinal) && p.Key.EndsWith("/def", StringComparison.Ordinal) && p.Value == GameFixtures.MugirlRaceDefName)
            .Select(p => ThingNumber(p.Key.Split('/')[1])).Where(n => n >= 0)];
        HashSet<int> aliveBefore = [.. seeded.Where(id => FindAnyPawn(id) is { Dead: false })];
        int start = Find.TickManager.TicksGame;
        yield return Wait.Ticks(TicksAfterUpgrade, timeoutSeconds: 500);
        report.Check(Find.TickManager.TicksGame - start >= TicksAfterUpgrade, $"the upgraded game runs {TicksAfterUpgrade} ticks", () => $"only {Find.TickManager.TicksGame - start}");

        // 死亡可能是原版的正常结果（播种时施加的 Hediff 继续发展）：只列出原因；Pawn 整个消失才算升级丢失了它。
        var vanished = new List<string>();
        foreach (int id in aliveBefore)
        {
            Pawn? pawn = FindAnyPawn(id);
            if (pawn is null || pawn.Discarded)
            {
                vanished.Add("Mugirl " + id.ToString(CultureInfo.InvariantCulture));
            }
            else if (pawn.Dead)
            {
                report.Info($"Mugirl {pawn.ThingID} died after loading: {DeathCause(pawn)}");
            }
        }

        report.Info($"{aliveBefore.Count} of {seeded.Count} seeded Mugirls were alive after loading");
        ReportList(report, "the seeded Mugirls alive after loading still exist", vanished);
    }

    private static IEnumerable<Wait> Variants(ValidationContext ctx, UpgradeState state)
    {
        ValidationReport report = ctx.Report;
        if (state.Kind == FixtureKind.Settings)
        {
            CheckSettings(ctx, state);
            if (state.Boot == 1)
            {
                yield break;
            }
        }

        if (state.Boot == 1 && state.Kind == FixtureKind.Alias)
        {
            CheckAliases(ctx, state);
            yield break;
        }

        if (state.Boot != 2 || state.ResaveDocument is null)
        {
            report.Skip("variants", $"no variant check for fixture kind {FixtureKinds.Text(state.Kind)} in boot {state.Boot}");
            yield break;
        }

        // X-newer：把本次的重存档的印记改成 Current+1，在本进程读入；升级器不得改动它、不得备份，恰好警告一次；再存档时保留该印记。
        if (state.Map.Schema < 0)
        {
            report.Fail("newer stamp", "the current schema is unknown (no upgrade manifest)");
            yield break;
        }

        var newer = new XDocument(state.ResaveDocument);
        int newerSchema = state.Map.Schema + 1;
        if (!SetStamp(newer, state.Map, newerSchema))
        {
            report.Fail("newer stamp", "the re-save has no " + StampLabel(state.Map) + " to raise");
            yield break;
        }

        string name = ValidationContext.SaveName("upgrade-newer");
        string path = GenFilePaths.FilePathForSavedGame(name);
        newer.Save(path);
        ctx.ExpectLog(entry => entry.Level == LogLevel.Warning && NewerWarning().IsMatch(entry.Message));
        int logStart = ctx.Logs.Count;
        yield return new LoadWait(name);
        LogCapture.DrainInto(ctx.Logs);
        int warnings = ctx.Logs.Skip(logStart).Count(e => e.Level == LogLevel.Warning && NewerWarning().IsMatch(e.Message));
        string? lastReport = ReadStaticString(ctx.RequireTarget(), DiagnosticsTypeName, "LastReportJson");
        JsonObject? diagnostics = lastReport is null ? null : JsonNode.Parse(lastReport) as JsonObject;
        string? newerState = (string?)diagnostics?["state"];
        report.Check((int?)diagnostics?["session"] is { } session && session > state.Session,
            "the second load publishes a fresh diagnostics session", () => $"previous session {state.Session}, current {(int?)diagnostics?["session"]}");
        report.Check(newerState == "Newer", "a save from a newer schema is recognised", () => "state " + (newerState ?? "-"));
        report.Check(TotalActions(diagnostics) == 0, "the upgrader made no edit to the newer save", () => $"{TotalActions(diagnostics)} action(s)");
        report.Check(warnings == 1, "exactly one warning names the newer save", () => $"{warnings} warning(s)");
        report.Check(BackupsOf(path).Count == 0 && (string?)diagnostics?["backup"] is null, "no backup is made of the newer save",
            () => string.Join(", ", BackupsOf(path)));

        // 保存时版本戳保留来源版本（decisions §2：会话没有完成升级时写来源版本），新版本的数据不会被标成当前版本。
        ctx.ExpectLog(entry => entry.Level == LogLevel.Warning && NewerWarning().IsMatch(entry.Message));
        var save = new SaveWait(ValidationContext.SaveName("upgrade-newer-resave"));
        yield return save;
        string? stamp = File.Exists(save.FilePath) ? StampIn(SaveXml.Load(save.FilePath), state.Map) : null;
        report.Check(stamp == newerSchema.ToString(CultureInfo.InvariantCulture), "saving the newer save keeps its stamp",
            () => $"stamp {stamp ?? "missing"}, expected {newerSchema}");
    }

    private static void CheckSettings(ValidationContext ctx, UpgradeState state)
    {
        ValidationReport report = ctx.Report;
        string[] files = Directory.Exists(GenFilePaths.ConfigFolderPath)
            ? Directory.GetFiles(GenFilePaths.ConfigFolderPath, "Mod_*_MugirlMod.xml")
            : [];
        if (!report.Check(files.Length == 1, "one settings file of the mod exists", () => $"{files.Length} file(s) in {GenFilePaths.ConfigFolderPath}"))
        {
            return;
        }

        if (state.Map.SettingsFileClass is not { } settingsClass)
        {
            report.Fail("settings", "the upgrade manifest (K1) has no settings.fileClass");
            return;
        }

        string text = File.ReadAllText(files[0]);
        report.Check(!text.Contains("\"" + settingsClass.Old + "\"", StringComparison.Ordinal), "the settings file no longer names the old settings class", () => settingsClass.Old);
        report.Check(text.Contains("\"" + settingsClass.New + "\"", StringComparison.Ordinal), "the settings file names the new settings class", () => settingsClass.New);
        List<string> leftovers = [.. state.Map.SettingsLabels.Where(l => l.New != l.Old && text.Contains("<" + l.Old + ">", StringComparison.Ordinal)).Select(l => l.Old)];
        ReportList(report, "the settings file holds no renamed or dropped key", leftovers);
        report.Check(!ctx.Logs.Any(e => e.Message.Contains("Could not find class", StringComparison.Ordinal)), "no class lookup failed while reading settings",
            () => "a \"Could not find class\" error was logged");

        string file = files[0];
        string hash = Sha256(file);
        long modified = File.GetLastWriteTimeUtc(file).Ticks;
        if (state.Boot == 2)
        {
            string expectedHash = ctx.Options.SuiteOption("upgrade.settingsBeforeHash", string.Empty);
            string expectedWrite = ctx.Options.SuiteOption("upgrade.settingsBeforeWriteUtcTicks", string.Empty);
            report.Check(expectedHash.Length > 0 && string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase)
                && modified.ToString(CultureInfo.InvariantCulture) == expectedWrite,
                "the next process does not rewrite the upgraded settings", () => "settings hash or write timestamp changed, or the launcher supplied no before snapshot");
        }

        // 真实入口已在构造阶段运行。再次调用同一文件升级器，返回 UpToDate 且字节/写入时间不变，证明升级已结束。
        MugirlTarget target = ctx.RequireTarget();
        Type? tablesType = target.FindType("Mugirl.SaveUpgrade.UpgradeTables");
        object? tables = tablesType?.GetProperty("Default", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        MethodInfo? run = target.FindType("Mugirl.SaveUpgrade.SettingsUpgrade")?.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic);
        if (!report.Check(run is not null && tables is not null, "the settings idempotence probe can invoke the file upgrader",
            () => "SettingsUpgrade.Run or UpgradeTables.Default is unavailable"))
        {
            return;
        }

        string? result = run!.Invoke(null, [file, tables])?.ToString();
        report.Check(result == "UpToDate" && hash == Sha256(file) && modified == File.GetLastWriteTimeUtc(file).Ticks,
            "the settings upgrade writes only on the first pass", () => $"repeated upgrade returned {result ?? "null"} or rewrote the settings file");
    }

    private static void CheckAliases(ValidationContext ctx, UpgradeState state)
    {
        ValidationReport report = ctx.Report;
        List<string> failures = [.. ctx.Logs.Where(e => e.IsErrorOrWorse && (e.Message.Contains("Could not find type", StringComparison.Ordinal)
            || e.Message.Contains("Could not find class", StringComparison.Ordinal))).Select(e => e.Describe())];
        ReportList(report, "the dependent mod's legacy XML class names resolve", failures);
        string? dependent = (string?)state.VariantManifest?["dependentPackageId"];
        ModContentPack? mod = dependent is null ? null : LoadedModManager.RunningModsListForReading.FirstOrDefault(m => string.Equals(m.PackageId, dependent, StringComparison.OrdinalIgnoreCase));
        if (!report.Check(mod is not null, "the dependent mod is loaded", () => dependent ?? "the variant fixture manifest names no dependentPackageId"))
        {
            return;
        }

        List<string> missing = [.. Strings(state.VariantManifest?["expectDefs"])
            .Where(name => !GenDefDatabase.AllDefTypesWithDatabases().Any(t => GenDefDatabase.GetDefSilentFail(t, name, specialCaseForSoundDefs: false)?.modContentPack == mod))];
        ReportList(report, "every def of the dependent mod is loaded", missing);
        var expectedTraders = Strings(state.VariantManifest?["expectDefs"]);
        foreach (string name in expectedTraders)
        {
            if (DefDatabase<TraderKindDef>.GetNamedSilentFail(name) is not { } trader || trader.modContentPack != mod)
            {
                continue;
            }

            bool aliasValid = FrozenXmlAliasEvidence.SlaveRange(ctx.RequireTarget().Assembly!, trader.stockGenerators ?? [], out string aliasDetail);
            report.Check(aliasValid, "the dependent XML creates the frozen alias bound to its canonical implementation and sets inherited slaveCountRange to 1~2",
                () => name + ": " + aliasDetail);
        }
    }

    private static void ReportConverterPath(ValidationContext ctx, UpgradeState state)
    {
        // 4979 实际只比较 build 数（忽略 rev），列表先用 PackageId、再用 FolderName 兜底；两者都保持顺序。
        string? written = state.EntryDocument.Root?.Element("meta")?.Element("gameVersion")?.Value.Trim();
        int savedBuild = written is null ? 0 : VersionControl.BuildFromVersionString(written);
        List<ModContentPack> active = [.. LoadedModManager.RunningMods];
        List<string> savedMods = SaveXml.ModIds(state.EntryDocument);
        bool sameMods = SaveEnvironment.ModListsMatch(savedMods, active.Select(m => m.PackageId))
            || SaveEnvironment.ModListsMatch(savedMods, active.Select(m => m.FolderName));
        bool sameBuild = savedBuild != 0 && savedBuild == VersionControl.CurrentBuild;
        bool skip = sameBuild && sameMods && !UnityData.isEditor;
        ctx.Report.Info($"vanilla converter path: {(skip ? "skipped" : "enabled")}; saved build {savedBuild}, running build {VersionControl.CurrentBuild}, "
            + $"ordered mod list {(sameMods ? "matches" : "differs")}, editor {UnityData.isEditor}; source meta version {written ?? "missing"}");
    }

    private static IEnumerable<(string Root, string Def, string CompClass)> CurrentComps()
    {
        foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            foreach (CompProperties props in def.comps)
            {
                if (props.compClass?.FullName is { } comp)
                {
                    yield return ("ThingDef", def.defName, comp);
                }
            }
        }

        foreach (HediffDef def in DefDatabase<HediffDef>.AllDefsListForReading)
        {
            foreach (HediffCompProperties props in def.comps ?? [])
            {
                if (props.compClass?.FullName is { } comp)
                {
                    yield return ("HediffDef", def.defName, comp);
                }
            }
        }
    }

    private static HashSet<string> AllDefNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Type defType in GenDefDatabase.AllDefTypesWithDatabases())
        {
            names.UnionWith(GenDefDatabase.GetAllDefsInDatabaseForDef(defType).Select(d => d.defName));
        }

        return names;
    }

    /// <summary>读档警告“Could not find think node with key N”中的 N（原版因此把这些工作的 lastJobGiverKey 写成 -1）。</summary>

    /// <summary>按 thingIDNumber 查找 Pawn：地图、世界、容器与尸体中的，活着或死去的。</summary>
    private static Pawn? FindAnyPawn(int thingId) => PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.FirstOrDefault(p => p.thingIDNumber == thingId);

    private static string DeathCause(Pawn pawn)
    {
        Hediff? lethal = pawn.health?.hediffSet?.hediffs.FirstOrDefault(h => h.def.lethalSeverity > 0f && h.Severity >= h.def.lethalSeverity);
        return lethal is not null ? "lethal hediff " + lethal.def.defName : pawn.health?.hediffSet?.hediffs.Any(h => h is Hediff_Injury) == true ? "injuries" : "unknown";
    }

    private static void CompareWithOffline(ValidationContext ctx, JsonObject? diagnostics)
    {
        ValidationReport report = ctx.Report;
        bool required = string.Equals(ctx.Options.SuiteOption("upgrade.requireOffline", "false"), "true", StringComparison.OrdinalIgnoreCase);
        if (ctx.Options.OfflineReportPath is not { } path)
        {
            if (required)
            {
                report.Fail("offline parity", "no offline --save-corpus report was given for this fixture (upgrade.requireOffline=true)");
            }
            else
            {
                report.Skip("offline parity", "no offline --save-corpus report was given for this fixture");
            }

            return;
        }

        if (ArtifactJson.TryRead(path, out string problem) is not JsonObject offline)
        {
            report.Fail("offline parity", problem.Length > 0 ? problem : path + " is not a JSON object");
            return;
        }

        ReportList(report, "the in-game pipeline made the same edits as the offline run", SaveActionParity.Compare(diagnostics, offline));
        // 实际引用解析才会删除的字典配对，在 ExpectedDocument 中逐节点回放，并与同 ID 的多条诊断求和核对。
    }

    /// <summary>
    /// DefInjected 文件中的键，按原版读取规则（DefInjectionPackage.AddDataFromFile/TryAddInjection）：元素名中的 [ ] 换成点，
    /// 旧语法的 rep 节点取其 path，defName 段经 BackCompatibleDefName。
    /// </summary>
    private static IEnumerable<string> InjectionKeys(RimWorld.IO.VirtualFile file, Type defType, List<string> problems)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(file.ReadAllText());
        }
        catch (Exception ex)
        {
            problems.Add($"{file.Name}: cannot parse: {ex.Message}");
            yield break;
        }

        foreach (XElement element in document.Root?.Elements() ?? [])
        {
            string raw = element.Name.LocalName == "rep" ? element.Element("path")?.Value ?? string.Empty : element.Name.LocalName;
            string key = raw.Contains('[', StringComparison.Ordinal) || raw.Contains(']', StringComparison.Ordinal) ? raw.Replace("]", string.Empty, StringComparison.Ordinal).Replace('[', '.') : raw;
            string[] parts = key.Split('.');
            parts[0] = BackCompatibility.BackCompatibleDefName(defType, parts[0], forDefInjections: true);
            yield return string.Join('.', parts);
        }
    }

    private static string? ReadUpgradeManifest(MugirlTarget target, out string problem)
    {
        MethodInfo? toJson = target.FindType(ManifestTypeName)?.GetMethod("ToJson", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
        if (toJson is null)
        {
            problem = $"{ManifestTypeName}.ToJson() does not exist in {target.Assembly?.GetName().Name ?? "the target"}";
            return null;
        }

        try
        {
            string json = (string?)toJson.Invoke(null, null) ?? "{}";
            _ = SaveNameMap.Parse(json);
            problem = string.Empty;
            return json;
        }
        catch (Exception ex)
        {
            problem = $"{ManifestTypeName}.ToJson() failed: {(ex as TargetInvocationException)?.InnerException ?? ex}";
            return null;
        }
    }

    private static HashSet<string> ReadTokenSet(string path, ValidationReport report)
    {
        if (!File.Exists(path))
        {
            report.Fail("token set", "missing: " + path);
            return [];
        }

        HashSet<string> tokens = [.. File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))];
        report.Info($"{tokens.Count} legacy token(s) from {path}");
        return tokens;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> RestraintDefaults(JsonNode? node)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        if (node is JsonObject map)
        {
            foreach ((string def, JsonNode? values) in map)
            {
                result[def] = ArtifactJson.StringMap(values);
            }
        }

        return result;
    }

    private static string StampLabel(SaveNameMap map) => map.StampLabel ?? DefaultStampLabel;

    private static XElement? StampElement(XDocument document, SaveNameMap map)
        => SaveXml.Game(document)?.Element("components")?.Elements("li").Select(li => li.Element(StampLabel(map))).FirstOrDefault(e => e is not null);

    private static string? StampIn(XDocument document, SaveNameMap map) => StampElement(document, map)?.Value.Trim();

    private static bool SetStamp(XDocument document, SaveNameMap map, int value)
    {
        if (StampElement(document, map) is not { } stamp)
        {
            return false;
        }

        stamp.Value = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>存档旁升级器写出的备份文件（<c>&lt;存档&gt;.mugirl-schema&lt;N&gt;.bak</c>）。</summary>
    private static List<string> BackupsOf(string? savePath)
    {
        if (savePath is null || Path.GetDirectoryName(savePath) is not { } folder || !Directory.Exists(folder))
        {
            return [];
        }

        return [.. Directory.GetFiles(folder, Path.GetFileName(savePath) + ".mugirl-schema*.bak")];
    }

    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static int? ReadInt(object? instance, string property)
        => instance?.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance) is int value ? value : null;

    private static string? ReadString(object? instance, string property)
        => instance?.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance) as string;

    private static string? ReadStaticString(MugirlTarget target, string typeName, string member)
    {
        Type? type = target.FindType(typeName);
        return (type?.GetProperty(member, BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? type?.GetField(member, BindingFlags.Public | BindingFlags.Static)?.GetValue(null)) as string;
    }

    private static List<(string Id, string Phase, long Actions, long Ms)> Steps(JsonObject? diagnostics)
    {
        var steps = new List<(string, string, long, long)>();
        if (diagnostics?["steps"] is JsonArray array)
        {
            foreach (JsonObject step in array.OfType<JsonObject>())
            {
                steps.Add(((string?)step["id"] ?? "?", (string?)step["phase"] ?? "?", (long?)step["actions"] ?? 0, (long?)step["ms"] ?? 0));
            }
        }

        return steps;
    }

    private static long TotalActions(JsonObject? diagnostics) => Steps(diagnostics).Sum(s => s.Actions);

    private static List<string> Strings(JsonNode? node)
        => node is JsonArray array ? [.. array.Select(n => (string?)n).OfType<string>()] : [];

    private static string Text(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static int ThingNumber(string thingId)
    {
        int start = thingId.Length;
        while (start > 0 && char.IsAsciiDigit(thingId[start - 1]))
        {
            start--;
        }

        return start < thingId.Length && int.TryParse(thingId[start..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : -1;
    }

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>逐条报告问题（最多 <see cref="MaxListed"/> 条），没有问题时记一条通过。</summary>
    private static void ReportList(ValidationReport report, string what, List<string> problems)
    {
        if (problems.Count == 0)
        {
            report.Pass(what);
            return;
        }

        foreach (string problem in problems.Take(MaxListed))
        {
            report.Fail(what, problem);
        }

        if (problems.Count > MaxListed)
        {
            report.Info($"{problems.Count - MaxListed} more problem(s) for \"{what}\"");
        }
    }

    /// <summary>升级器读到印记高于当前版本的存档时记的警告（SaveUpgradeRuntime：“… written by a newer Mugirl …”）。</summary>
    [GeneratedRegex(@"written by a newer Mugirl", RegexOptions.CultureInvariant)]
    private static partial Regex NewerWarning();


    /// <summary>步骤之间共享的状态。</summary>
    private sealed class UpgradeState
    {
        public int Session { get; set; } = -1;
        public string? ReadTraceJson { get; set; }
        public string? VanillaReadJson { get; set; }
        public string? PriceReadJson { get; set; }
        public string? QualityReadJson { get; set; }
        public MissingComponentProjection? MissingComponents { get; set; }
        public Dictionary<string, SerializationPawnDefaults> SerializationPawnDefaults { get; set; } = new(StringComparer.Ordinal);
        public JsonObject? Diagnostics { get; set; }
        public int? CompletionLetterId { get; set; }
        private XDocument? entryDocument;
        private XDocument? resaveDocument;

        public int Boot { get; set; } = 1;

        public FixtureRef? Fixture { get; set; }

        public FixtureKind Kind { get; set; }

        /// <summary>本次读入的是旧档，升级器必须运行（旧档夹具的第 1 次启动）。</summary>
        public bool Upgrading { get; set; }

        public JsonObject? FixtureManifest { get; set; }

        /// <summary>X-* 夹具自己的清单（依赖 Mod、期待的 Def）。</summary>
        public JsonObject? VariantManifest { get; set; }

        public string? EntryPath { get; set; }

        /// <summary>读入的存档（只解析一次）；比较时复制后再翻译。</summary>
        public XDocument EntryDocument => entryDocument ??= SaveXml.Load(EntryPath!);

        public string? ResavePath { get; set; }

        /// <summary>读档后立即写出的重存档（只解析一次）；没有重存档时为 null。</summary>
        public XDocument? ResaveDocument => ResavePath is null ? null : resaveDocument ??= SaveXml.Load(ResavePath);

        public SaveNameMap Map { get; set; } = SaveNameMap.Empty;

        /// <summary>升级清单原文（K1）；不可用时为 null。</summary>
        public string? ManifestJson { get; set; }

        public SaveOwnerCatalog? Catalog { get; set; }

        public HashSet<string>? TokenSet { get; set; }

        public List<string> LegacyDefs { get; set; } = [];

        public List<string> LegacyThingDefs { get; set; } = [];

        public Dictionary<string, IReadOnlyDictionary<string, string>> RestraintDefaults { get; set; } = new(StringComparer.Ordinal);

        public List<string> CurrentDefs { get; set; } = [];

        public List<string> CurrentThingDefs { get; set; } = [];

        /// <summary>当前运行中所有 Def 的名字（任意 Mod 与原版）：旧名单排除仍在使用的名字。</summary>
        public HashSet<string> AllCurrentDefNames { get; set; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// 重存档中残留旧名的扫描，只看 Mod 拥有的位置（critique §4.3(5)）：<c>Class</c> 值（任意位置）；Def 引用位置（<see cref="SaveDefRoots"/>）
    /// 与 ThingID、<c>Thing_</c> 引用；标签按清单标签行的 owner 选择器（只在选中的元素下）；枚举成员与保存的值只在本 Mod 的节点内
    /// （本 Mod 的 Class 节点、本 Mod 的物品、本 Mod 的组件标签）。给出旧名单文件时，其中的名字按 Class、Def 引用与本 Mod 节点内的值和标签检查。
    /// </summary>
    private sealed class TokenScan
    {
        private readonly UpgradeState state;
        private readonly HashSet<string> types;
        private readonly HashSet<string> defs;
        private readonly HashSet<string> values;
        private readonly Dictionary<string, List<SaveLabelRow>> labelRows;
        private readonly HashSet<string>? flat;
        private readonly HashSet<string> currentModDefs;

        public TokenScan(UpgradeState state)
        {
            this.state = state;
            flat = state.TokenSet;
            currentModDefs = new HashSet<string>(state.CurrentDefs, StringComparer.Ordinal);
            types = state.Map.LegacyOnlyTypes();
            defs = state.Map.LegacyOnlyDefs(state.AllCurrentDefNames);
            values = state.Map.LegacyOnlyValues();
            labelRows = state.Map.LabelRows.Where(r => r.Fate.Kind is SaveLabelFateKind.Renamed or SaveLabelFateKind.Dropped or SaveLabelFateKind.Consumed)
                .GroupBy(r => r.Old, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        }

        public bool IsEmpty => flat is null && types.Count == 0 && state.Map.DefCount == 0 && values.Count == 0 && labelRows.Count == 0;

        public List<string> Hits { get; } = [];

        public string Describe()
            => flat is not null
                ? $"{flat.Count} token(s) from the token set file"
                : $"{types.Count} type(s), {defs.Count} def name(s), {values.Count} saved value(s) and enum member(s), {labelRows.Values.Sum(r => r.Count)} owner-scoped label(s) from the upgrade manifest";

        public void Run(XElement game) => Visit(game, "game", modOwned: false);

        private void Visit(XElement element, string path, bool modOwned)
        {
            foreach (XElement child in element.Elements())
            {
                string childPath = path + "/" + child.Name.LocalName;
                string? className = SaveXml.ClassOf(child);

                // 本 Mod 的节点：本 Mod 的 Class、def 属于本 Mod 的节点（物品、Hediff、工作……），以及物品节点里的本 Mod 组件标签。
                bool owned = modOwned || SaveXml.IsModClass(className)
                    || (SaveXml.Def(child) is { } def && currentModDefs.Contains(def))
                    || (SaveXml.IsThing(element) && SaveXml.Def(element) is { } ownerDef && state.Catalog!.IsModCompLabel(ownerDef, child.Name.LocalName));
                if (className is not null)
                {
                    foreach (string part in className.Split(['<', '>', ',', '[', ']', ' ', '`'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (types.Contains(part) || flat?.Contains(part) == true)
                        {
                            Hits.Add($"Class {part} at {childPath}");
                        }
                    }
                }

                CheckLabel(child, element, childPath, owned);
                if (!child.HasElements)
                {
                    CheckValue(child, childPath, owned);
                }
                else
                {
                    Visit(child, childPath, owned);
                }
            }
        }

        private void CheckLabel(XElement child, XElement owner, string path, bool modOwned)
        {
            string label = child.Name.LocalName;
            if (flat is not null)
            {
                if (modOwned && SaveXml.IsModClass(SaveXml.ClassOf(owner)) && flat.Contains(label))
                {
                    Hits.Add($"label {label} at {path}");
                }

                return;
            }

            if (labelRows.TryGetValue(label, out List<SaveLabelRow>? rows) && rows.FirstOrDefault(r => r.Owner.Matches(owner, state.Catalog!)) is { } row)
            {
                Hits.Add($"label {label} at {path} ({row.Fate})");
            }
        }

        private void CheckValue(XElement leaf, string path, bool modOwned)
        {
            string text = leaf.Value;
            if (state.Map.IsLegacyValue(leaf, state.Catalog!))
            {
                Hits.Add($"value {text} at {path}");
                return;
            }

            if (text.Length == 0 || text.Length > 160 || text.Any(char.IsWhiteSpace))
            {
                return;
            }

            string? defRoot = SaveDefRoots.RootOf(leaf);
            bool defPosition = defRoot is not null;
            bool identityPosition = leaf.Name.LocalName == "id" && leaf.Parent is { } thing && SaveXml.IsThing(thing);
            string idDef = ThingIdDef(text);
            if (flat is not null)
            {
                if ((defPosition || identityPosition) && (flat.Contains(text) || flat.Contains(idDef)))
                {
                    Hits.Add($"value {text} at {path}");
                }

                return;
            }

            if (defRoot is not null && state.Map.IsLegacyDef(defRoot, text))
            {
                Hits.Add($"def {defRoot}/{text} at {path}");
            }
            else if (identityPosition && idDef.Length > 0 && idDef != text && state.Map.IsLegacyDef("ThingDef", idDef))
            {
                Hits.Add($"thing ID {text} at {path}");
            }
        }

        // ThingID 与 Thing_ 引用的 defName 部分（去掉前缀与结尾数字）；不像 ThingID 时为空串。
        private static string ThingIdDef(string text)
        {
            string body = text.StartsWith("Thing_", StringComparison.Ordinal) ? text[6..] : text;
            string def = body.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            return def.Length > 0 && def.Length < body.Length ? def : string.Empty;
        }
    }
}
