using System.Text.RegularExpressions;
using MugirlRace.Validation.Legacy;
using MugirlRace.Validation.Metadata;
using Mono.Cecil;

namespace MugirlRace.Validation.SaveUpgrade;

/// <summary>
/// 给每条旧语料行一个处置：Unchanged（新构建以同样的形态读写）、Renamed（清单映射到存在的目标）、Consumed（某个步骤声明消费，并由
/// 黄金样例证明）、Dropped（带策略与理由）或 undisposed。同时检查清单本身（过期键、重复、链、名字复用、缺理由、未证明的消费、
/// 领域代码中残留的旧名、步骤测试、Def 名规则）。
/// </summary>
/// <remarks>
/// <para>
/// 规则（spec/critique-validation.md F10）：Unchanged 要求 Scribe 种类、值类型、LookMode 与默认值都相同；默认值变化需要
/// <c>defaults</c> 物化条目。MooGirl 时代（≤0.4.4）只按清单 <c>moogirlEra</c> 的前缀与模式整体丢弃（决定 §5 D3）。
/// </para>
/// <para>
/// 清单（契约 K1）用新名书写：标签与默认值的所属选择器经 <see cref="OwnerResolver"/> 映射回旧 owner，枚举行的枚举名是新名，
/// 值行按上下文名登记（上下文没有导出时按值本身匹配）。类型与 Def 行用旧名作键。
/// </para>
/// </remarks>
internal sealed partial class DispositionEngine
{
    private static readonly string[] ComponentRoots = ["Verse.GameComponent", "Verse.MapComponent", "RimWorld.Planet.WorldComponent"];

    // 写在独立文档（剧本、意识形态、异种文件）里的对象：那些文档不经过 DOM 升级，改名后只能靠 GetBackCompatibleType 后缀找到（决定 D4）。
    private static readonly string[] SideDocumentRoots =
    [
        "RimWorld.ScenPart", "RimWorld.Precept", "RimWorld.RitualBehaviorWorker", "RimWorld.RitualOutcomeEffectWorker", "RimWorld.RitualObligationTargetFilter",
        "RimWorld.RitualTargetFilter", "RimWorld.RitualRole", "RimWorld.RitualAttachableOutcomeEffectWorker", "RimWorld.IdeoFoundation", "RimWorld.RitualPosition",
        "RimWorld.RitualSpectatorFilter", "RimWorld.RitualStage", "RimWorld.RitualRoleAssignments",
    ];

    private readonly LegacyCorpus corpus;
    private readonly NewBuild build;
    private readonly UpgradeManifest? manifest;
    private readonly GoldenSteps golden;
    private readonly ModuleMap modules;
    private readonly IReadOnlySet<string> publicTypes;
    private readonly Dictionary<string, ManifestEntry> typeMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Root, string Name), ManifestEntry> defMap = [];
    private readonly Dictionary<(string Owner, string Label), ManifestEntry> labelMap = [];
    private readonly Dictionary<(string Enum, string Member), ManifestEntry> enumMap = [];
    private readonly Dictionary<string, List<ManifestEntry>> valueMap = new(StringComparer.Ordinal);
    private readonly HashSet<(string Owner, string Label, string Default)> materialized = [];
    private readonly Dictionary<string, ManifestStep> steps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Step, string? Problem)> consumed = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Owner, string Label), (string Step, string? Problem)> consumedLabels = [];
    private readonly Dictionary<string, ManifestEntry> jobPolicies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ManifestEntry> lordPolicies = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> legacyTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> requiredLegacyTypes = new(StringComparer.Ordinal);
    private readonly HashSet<(string Owner, string Label)> legacyLabels = [];
    private readonly List<(ManifestEntry Entry, IReadOnlySet<string> Owners)> labelEntries = [];
    private readonly HashSet<string> staleLabelTokens = new(StringComparer.Ordinal);
    private OwnerResolver? resolver;

    public DispositionEngine(LegacyCorpus corpus, NewBuild build, UpgradeManifest? manifest, GoldenSteps golden, ModuleMap modules, IReadOnlySet<string> publicTypes)
    {
        this.corpus = corpus;
        this.build = build;
        this.manifest = manifest;
        this.golden = golden;
        this.modules = modules;
        this.publicTypes = publicTypes;
        foreach (CorpusRow row in corpus.Rows(LegacyCorpus.TypesFile))
        {
            legacyTypes.Add(row[0]);
            if (row.IsRequired)
            {
                requiredLegacyTypes.Add(row[0]);
            }
        }

        foreach (CorpusRow row in corpus.Rows(LegacyCorpus.ObservedFile).Concat(corpus.Rows(LegacyCorpus.ExtraFile)).Where(r => r[0] is "type" or "type?"))
        {
            legacyTypes.Add(row[1]);
            requiredLegacyTypes.Add(row[1]);
        }

        legacyLabels.UnionWith(corpus.Rows(LegacyCorpus.LabelsFile).Select(r => (r[0], r[1])));
        legacyLabels.UnionWith(corpus.Rows(LegacyCorpus.ObservedFile).Concat(corpus.Rows(LegacyCorpus.ExtraFile)).Where(r => r[0] == "label").Select(r => (r[2], r[1])));
    }

    /// <summary>每条语料行的处置。</summary>
    public List<CoverageRow> Coverage { get; } = [];

    /// <summary>清单与代码的卫生问题（总是失败）。</summary>
    public List<(string Subject, string Message)> Failures { get; } = [];

    /// <summary>当前构建仍然以同样的名字存在的语料行键（类型、Def、标签）：重存扫描不能把它们当作只属于旧构建的名字。</summary>
    public HashSet<string> StillPresent { get; } = new(StringComparer.Ordinal);

    private OwnerResolver Resolver => resolver ??= new OwnerResolver(corpus, legacyTypes, MapType);

    public void Run()
    {
        Index();
        foreach (CorpusRow row in corpus.Rows(LegacyCorpus.TypesFile))
        {
            if (build.Types.ContainsKey(row[0]))
            {
                StillPresent.Add(row.Key);
            }

            if (row[2] == "-")
            {
                // 不以类名写进存档的类型（comp、props、工具类、抽象类）：名字只经由标签、Def 与 XML 出现，不需要处置。
                continue;
            }

            Add(row, Type(row[0], row[2]));
        }

        foreach (CorpusRow row in corpus.Rows(LegacyCorpus.LabelsFile))
        {
            Add(row, Label(row[0], row[1], row));
        }

        foreach (CorpusRow row in corpus.Rows(LegacyCorpus.EnumsFile))
        {
            Add(row, Enum(row[0], row[1]));
        }

        foreach (CorpusRow row in corpus.Rows(LegacyCorpus.DefsFile))
        {
            if (build.HasDef(row[0], row[1]))
            {
                StillPresent.Add(row.Key);
            }

            Verdict verdict = Def(row[0], row[1], row[2]);
            // D3 明确不支持 MooGirl；不能为了清零清单，把无时代特征的通用名字扩成全局删除标记。
            // 只有语料证明完全属于该时代、又没有映射/现存定义/可靠标记的行，才留给原版缺失 Def 处理。
            Add(row, row.IsMooGirlOnly && verdict.Disposition == "undisposed"
                ? Verdict.NotApplicable("Unsupported MooGirl-only name without a safe era marker; vanilla missing-Def handling (decision D3)")
                : verdict);
        }

        foreach (CorpusRow row in corpus.Rows(LegacyCorpus.ToolsFile))
        {
            Add(row, Tool(row[0], row[1]));
        }

        foreach (CorpusRow row in corpus.Rows(LegacyCorpus.SavedValuesFile))
        {
            Add(row, Value(row[0], row[1], row[2]));
        }

        foreach (CorpusRow row in corpus.Rows(LegacyCorpus.LoadIdsFile))
        {
            Add(row, LoadId(row[0], row[1]));
        }

        foreach (CorpusRow row in corpus.Rows(LegacyCorpus.SettingsFile))
        {
            Add(row, Setting(row[0], row[1], row[3], row.IsMooGirlOnly));
        }

        foreach (CorpusRow row in corpus.Rows(LegacyCorpus.JobsLordsFile))
        {
            Add(row, JobOrLord(row[0], row[1], row[2]));
        }

        foreach (CorpusRow row in corpus.Rows(LegacyCorpus.ObservedFile).Concat(corpus.Rows(LegacyCorpus.ExtraFile)))
        {
            if ((row[0] is "type" or "type?" && build.Types.ContainsKey(row[1])) || (row[0] is "def" or "thingid" && build.HasDef("*", row[1])))
            {
                StillPresent.Add(row.Key);
            }

            Add(row, Observed(row[0], row[1], row[2]));
        }

        CheckStaleEntries();
        CheckLeftoverLiterals();
        CheckStepTests();
    }

    private void Add(CorpusRow row, Verdict verdict)
    {
        string module = verdict.Module ?? ModuleOf(row);
        Coverage.Add(new CoverageRow(row.Key, row.Tiers, verdict.Disposition, verdict.Target, verdict.Via, module, verdict.Detail));
    }

    // ---- 清单索引与卫生 ----

    private void Index()
    {
        if (manifest is null)
        {
            return;
        }

        golden.ConfigureOwners(corpus, manifest);

        foreach (string problem in manifest.Problems)
        {
            Failures.Add(("manifest", problem));
        }

        foreach (ManifestStep step in manifest.Steps)
        {
            if (!steps.TryAdd(step.Id, step))
            {
                Failures.Add(("step " + step.Id, "Duplicate step id in the manifest."));
            }

            if (step.Reason.Trim().Length == 0)
            {
                Failures.Add(("step " + step.Id, "Step without a reason."));
            }
        }

        foreach (ManifestEntry entry in manifest.Types)
        {
            Unique(typeMap, entry.Old, entry);
            RequireReason(entry);
        }

        foreach (ManifestEntry entry in manifest.Defs)
        {
            Unique(defMap, (entry.Root ?? "*", entry.Old), entry);
            if (entry.LegacyRoot is { Length: > 0 } legacyRoot && legacyRoot != entry.Root)
            {
                Unique(defMap, (legacyRoot, entry.Old), entry);
            }

            RequireReason(entry);
        }

        // 消费令牌：type/def/enum/value 按文本，label 按选择器解析出的旧 owner（类型映射已经建好）。
        foreach (ManifestStep step in manifest.Steps)
        {
            foreach (string token in step.Consumes)
            {
                string? problem = golden.Verify(step.Id, token);
                if (problem is not null)
                {
                    Failures.Add(($"step {step.Id} consumes {token}", "Unverified consumption: " + problem + "."));
                }

                if (token.StartsWith("label:", StringComparison.Ordinal))
                {
                    (string selector, string label) = SplitLabelToken(token);
                    (IReadOnlySet<string> owners, string? selectorProblem) = Resolver.Resolve(selector, label);
                    if (selectorProblem is not null)
                    {
                        Failures.Add(($"step {step.Id} consumes {token}", "Unreadable owner selector: " + selectorProblem));
                    }

                    List<string> writers = owners.Where(owner => legacyLabels.Contains((owner, label))).ToList();
                    if (writers.Count == 0)
                    {
                        staleLabelTokens.Add(token);
                    }

                    foreach (string owner in writers)
                    {
                        consumedLabels.TryAdd((owner, label), (step.Id, problem));
                    }
                }

                consumed.TryAdd(token, (step.Id, problem));
            }
        }

        foreach (ManifestEntry entry in manifest.Labels)
        {
            (IReadOnlySet<string> owners, string? problem) = Resolver.Resolve(entry.Owner ?? "", entry.Old);
            if (problem is not null)
            {
                Failures.Add((entry.ToString(), "Unreadable owner selector: " + problem));
            }

            HashSet<string> writers = owners.Where(owner => legacyLabels.Contains((owner, entry.Old))).ToHashSet(StringComparer.Ordinal);
            labelEntries.Add((entry, writers));
            foreach (string owner in writers)
            {
                Unique(labelMap, (owner, entry.Old), entry);
            }

            RequireReason(entry);
            if (entry.Disposition == "consumed" && (entry.Step is null || !steps.ContainsKey(entry.Step)))
            {
                Failures.Add((entry.ToString(), $"Consumed by step '{entry.Step}', which the manifest does not declare."));
            }
        }

        foreach (ManifestEntry entry in manifest.Enums)
        {
            Unique(enumMap, (entry.Owner ?? "", entry.Old), entry);
        }

        foreach (ManifestEntry entry in manifest.Values)
        {
            if (!valueMap.TryGetValue(entry.Old, out List<ManifestEntry>? list))
            {
                valueMap[entry.Old] = list = [];
            }

            list.Add(entry);
        }

        foreach (ManifestEntry entry in manifest.Defaults)
        {
            (IReadOnlySet<string> owners, string? problem) = Resolver.Resolve(entry.Owner ?? "", entry.Label);
            if (problem is not null)
            {
                Failures.Add((entry.ToString(), "Unreadable owner selector: " + problem));
            }

            foreach (string owner in owners)
            {
                materialized.Add((owner, entry.Label ?? "", entry.Old));
            }
        }

        foreach ((IReadOnlyList<ManifestEntry> list, Dictionary<string, ManifestEntry> map, string[] allowed) in new[]
        {
            (manifest.JobPolicies, jobPolicies, new[] { "endInFlight", "preserve" }),
            (manifest.LordPolicies, lordPolicies, new[] { "resetGraph", "removeLord", "preserve" }),
        })
        {
            foreach (ManifestEntry entry in list)
            {
                Unique(map, entry.Old, entry);
                string policy = entry.Policy ?? "";
                if (!allowed.Contains(policy, StringComparer.Ordinal) && !(policy.StartsWith("claimed:", StringComparison.Ordinal) && steps.ContainsKey(policy["claimed:".Length..])))
                {
                    Failures.Add((entry.ToString(), $"Policy '{policy}' is neither {string.Join(" nor ", allowed)} nor claimed:<a declared step id>."));
                }
            }
        }

        // 链：A→B 而 B 又是另一个键（地图必须先组合成 A→C）。
        foreach (ManifestEntry entry in manifest.Types.Where(e => e.New is not null && !e.New.Equals(e.Old, StringComparison.OrdinalIgnoreCase) && typeMap.ContainsKey(e.New)))
        {
            Failures.Add((entry.ToString(), $"Uncomposed chain: {entry.New} is itself renamed by the manifest; map {entry.Old} directly to the final name."));
        }

        foreach (ManifestEntry entry in manifest.Defs.Where(e => e.New is not null && e.New != e.Old && defMap.ContainsKey((e.Root ?? "*", e.New))))
        {
            Failures.Add((entry.ToString(), $"Uncomposed chain: {entry.New} is itself renamed by the manifest."));
        }

        foreach ((ManifestEntry entry, IReadOnlySet<string> owners) in labelEntries.Where(pair => pair.Entry.New is { } target && target != pair.Entry.Old))
        {
            if (owners.Any(owner => labelMap.TryGetValue((owner, entry.New!), out ManifestEntry? next) && next != entry))
            {
                Failures.Add((entry.ToString(), $"Uncomposed chain: label '{entry.New}' of the same owner is itself renamed by the manifest (rename both in one step or compose the chain)."));
            }
        }

        foreach (ManifestEntry entry in manifest.Enums.Where(e => e.New is not null && e.New != e.Old && enumMap.ContainsKey((e.Owner ?? "", e.New))))
        {
            Failures.Add((entry.ToString(), $"Uncomposed chain: member {entry.New} of {entry.Owner} is itself renamed by the manifest."));
        }
    }

    // label:<选择器>/<标签>：标签不含 '/'，选择器（Path、Document）可以含。
    private static (string Selector, string Label) SplitLabelToken(string token)
    {
        string body = token["label:".Length..];
        int slash = body.LastIndexOf('/');
        return slash <= 0 ? (body, "") : (body[..slash], body[(slash + 1)..]);
    }

    // value:<上下文>/<值>：上下文名不含 '/'，值（路径、键）可以含。
    private static (string Context, string Value) SplitValueToken(string token)
    {
        string body = token["value:".Length..];
        int slash = body.IndexOf('/', StringComparison.Ordinal);
        return slash <= 0 ? ("", body) : (body[..slash], body[(slash + 1)..]);
    }

    private void Unique<TKey>(Dictionary<TKey, ManifestEntry> map, TKey key, ManifestEntry entry)
        where TKey : notnull
    {
        if (!map.TryAdd(key, entry) && map[key] != entry)
        {
            Failures.Add((entry.ToString(), $"Duplicate key: {map[key]} maps the same legacy name."));
        }
    }

    private void RequireReason(ManifestEntry entry)
    {
        if (entry.Reason.Trim().Length == 0)
        {
            Failures.Add((entry.ToString(), "Entry without a reason."));
        }

        if (entry.Disposition == "drop" && string.IsNullOrWhiteSpace(entry.Policy) && entry.Section is "types" or "defs")
        {
            Failures.Add((entry.ToString(), "Drop without a node policy (what happens to saved objects and references)."));
        }
    }

    // 清单中的每个键都必须是语料中的名字（过期条目说明映射与语料脱节）。
    private void CheckStaleEntries()
    {
        if (manifest is null)
        {
            return;
        }

        var defNames = corpus.Rows(LegacyCorpus.DefsFile).Select(r => (r[0], r[1])).ToHashSet();
        var observedDefs = corpus.Rows(LegacyCorpus.ObservedFile).Concat(corpus.Rows(LegacyCorpus.ExtraFile))
            .Where(r => r[0] is "def" or "thingid" or "def-pattern").Select(r => r[1]).ToHashSet(StringComparer.Ordinal);
        var enums = corpus.Rows(LegacyCorpus.EnumsFile).Select(r => (MapType(r[0]) ?? r[0], r[1])).ToHashSet();
        enums.UnionWith(corpus.Rows(LegacyCorpus.EnumsFile).Select(r => (r[0], r[1])));
        var values = corpus.Rows(LegacyCorpus.SavedValuesFile).Select(r => Unquote(r[1])).ToHashSet(StringComparer.Ordinal);
        var jobs = corpus.Rows(LegacyCorpus.JobsLordsFile).Select(r => r[1]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        jobs.UnionWith(corpus.Rows(LegacyCorpus.JobsLordsFile).Select(r => MapType(r[1])).OfType<string>());
        foreach (ManifestEntry entry in manifest.Types.Where(e => !legacyTypes.Contains(e.Old, StringComparer.OrdinalIgnoreCase)))
        {
            Failures.Add((entry.ToString(), "Stale entry: no legacy build or save contains this type name."));
        }

        foreach (ManifestEntry entry in manifest.Defs.Where(e => !((e.LegacyRoot ?? e.Root) is null or "*" ? defNames.Any(d => d.Item2 == e.Old) : defNames.Contains((e.LegacyRoot ?? e.Root!, e.Old))) && !observedDefs.Contains(e.Old)))
        {
            Failures.Add((entry.ToString(), "Stale entry: no legacy content tree or save contains this def."));
        }

        foreach ((ManifestEntry entry, IReadOnlySet<string> owners) in labelEntries.Where(pair => pair.Owners.Count == 0))
        {
            Failures.Add((entry.ToString(), $"Stale entry: no legacy owner selected by {entry.Owner} writes label '{entry.Old}'."));
        }

        foreach (ManifestEntry entry in manifest.Enums.Where(e => !enums.Contains((e.Owner ?? "", e.Old))))
        {
            Failures.Add((entry.ToString(), $"Stale entry: {entry.Owner}.{entry.Old} is not a saved legacy enum member."));
        }

        foreach (ManifestEntry entry in manifest.Values.Where(e => !values.Contains(e.Old)))
        {
            Failures.Add((entry.ToString(), $"Stale entry: no legacy build stores '{entry.Old}' into a saved string field."));
        }

        foreach (ManifestEntry entry in manifest.JobPolicies.Concat(manifest.LordPolicies).Where(e => !jobs.Contains(e.Old)))
        {
            Failures.Add((entry.ToString(), "Stale policy: no legacy build defines this JobDriver/LordJob type."));
        }

        foreach ((string token, (string step, _)) in consumed)
        {
            if (!IsCorpusToken(token, defNames, enums, values))
            {
                Failures.Add(($"step {step} consumes {token}", "Stale consumption: the token names nothing in the legacy corpus."));
            }
        }
    }

    private bool IsCorpusToken(string token, HashSet<(string, string)> defNames, HashSet<(string, string)> enums, HashSet<string> values)
    {
        int colon = token.IndexOf(':', StringComparison.Ordinal);
        string kind = colon < 0 ? "" : token[..colon];
        string body = colon < 0 ? "" : token[(colon + 1)..];
        int slash = body.IndexOf('/', StringComparison.Ordinal);
        int dot = body.LastIndexOf('.');
        return kind switch
        {
            "type" => legacyTypes.Contains(body, StringComparer.OrdinalIgnoreCase),
            "def" => slash > 0 && (defNames.Contains((body[..slash], body[(slash + 1)..])) || (body[..slash] == "*" && defNames.Any(d => d.Item2 == body[(slash + 1)..]))),
            "label" => !staleLabelTokens.Contains(token),
            "enum" => dot > 0 && enums.Contains((body[..dot], body[(dot + 1)..])),
            "value" => values.Contains(SplitValueToken(token).Value),
            _ => false,
        };
    }

    // 已改名、丢弃或被消费的旧类型名不应再出现在升级层之外的字面量中（领域代码里的旧名读取者）。
    private void CheckLeftoverLiterals()
    {
        foreach (CoverageRow row in Coverage.Where(r => r.Disposition is "renamed" or "dropped" or "consumed" && r.Key.StartsWith(LegacyCorpus.TypesFile + "|", StringComparison.Ordinal)))
        {
            string type = row.Key.Split('|')[1];
            if (build.LiteralSites.TryGetValue(type, out string? site))
            {
                Failures.Add((type, $"Legacy type name used as a string outside {NewBuild.UpgradeNamespace} ({site}); only the upgrade layer may know old names."));
            }
        }
    }

    // 每个步骤都有测试：DOM 步骤有黄金样例 Golden/<id>/{before,after}.xml，其他阶段的步骤至少被一个测试源文件按 id 引用（设计 §6）。
    private void CheckStepTests()
    {
        foreach (ManifestStep step in steps.Values)
        {
            if (step.Phase == "dom")
            {
                if (!golden.Has(step.Id))
                {
                    Failures.Add(("step " + step.Id, $"DOM step without golden fixtures {Path.Combine("Golden", step.Id)}/before.xml and after.xml (every step is tested, design §6)."));
                }
            }
            else if (!golden.TestsMention(step.Id))
            {
                Failures.Add(("step " + step.Id, $"Phase {step.Phase} step that no test under Source/Tests/MugirlRace.Tests mentions by id (every step is tested, design §6)."));
            }
        }
    }

    // ---- 类型 ----

    /// <summary>旧类型名的新名；丢弃或被消费时为 <see langword="null"/>，未映射时为原名。</summary>
    private string? MapType(string legacy)
        => typeMap.TryGetValue(legacy, out ManifestEntry? entry) ? (entry.Disposition == "rename" ? entry.New : null)
            : consumed.ContainsKey("type:" + legacy) || IsMooGirlType(legacy) ? null
            : legacy;

    private bool IsMooGirlType(string type) => manifest is not null && manifest.MooGirlTypePrefixes.Any(prefix => type.StartsWith(prefix, StringComparison.Ordinal));

    private bool IsMooGirlDef(string defName) => manifest is not null && manifest.MooGirlDefPatterns.Any(pattern => pattern.IsMatch(defName));

    private Verdict Type(string legacy, string legacyRoot)
    {
        if (typeMap.TryGetValue(legacy, out ManifestEntry? entry))
        {
            if (entry.Disposition == "drop")
            {
                return Verdict.Dropped(entry.Policy ?? "", entry.ToString(), entry.Module);
            }

            string target = entry.New ?? "";
            CheckTypeTarget(legacy, legacyRoot, target, entry);
            return Verdict.Renamed(target, entry.ToString(), entry.Module);
        }

        if (Consumption("type:" + legacy) is { } step)
        {
            return Verdict.Consumed(step);
        }

        if (IsMooGirlType(legacy))
        {
            return Verdict.Dropped("MooGirl era", "moogirlEra.typePrefixes", null);
        }

        if (!build.Types.TryGetValue(legacy, out TypeDefinition? current))
        {
            return Verdict.Undisposed("no type of this name in the new build and no manifest entry");
        }

        string? root = build.Roots.RootOf(current);
        return legacyRoot == "-" || root == legacyRoot
            ? Verdict.Unchanged()
            : Verdict.Undisposed($"save root changed from {legacyRoot} to {root ?? "(not save-visible)"}");
    }

    private void CheckTypeTarget(string legacy, string legacyRoot, string target, ManifestEntry entry)
    {
        if (!build.Types.TryGetValue(target, out TypeDefinition? type))
        {
            Failures.Add((entry.ToString(), $"Target type {target} does not exist in the new build."));
            return;
        }

        if (legacyRoot != "-")
        {
            if (type.IsAbstract)
            {
                Failures.Add((entry.ToString(), $"Target {target} is abstract; saved objects cannot be created."));
            }

            string? root = build.Roots.RootOf(type);
            if (root != legacyRoot)
            {
                Failures.Add((entry.ToString(), $"Target {target} is saved under {root ?? "(no save root)"}, the legacy type under {legacyRoot}."));
            }

            if (Array.IndexOf(ComponentRoots, legacyRoot) >= 0)
            {
                string parameter = legacyRoot switch
                {
                    "Verse.GameComponent" => "Verse.Game",
                    "Verse.MapComponent" => "Verse.Map",
                    _ => "RimWorld.Planet.World",
                };
                if (!type.Methods.Any(m => m.IsConstructor && !m.IsStatic && m.IsPublic && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == parameter))
                {
                    Failures.Add((entry.ToString(), $"Target {target} lacks the public ({parameter}) constructor the game uses to create it."));
                }
            }

            if (CecilNames.BaseTypeNames(type).FirstOrDefault(name => SideDocumentRoots.Contains(name, StringComparer.Ordinal)) is { } sideRoot && !build.PatchesBackCompatibleType)
            {
                Failures.Add((entry.ToString(), $"{target} derives from {sideRoot}, which scenario/ideo/xenotype files store outside saves: renaming it needs the "
                    + "Verse.BackCompatibility.GetBackCompatibleType postfix in SaveUpgrade/Hooks (decisions D4), which the build does not have."));
            }
        }

        if (build.Types.ContainsKey(legacy) && legacy != target)
        {
            Failures.Add((entry.ToString(), $"The legacy class {legacy} still exists: saves keep resolving to it (and Fill*Components would add a stray instance)."));
        }

        // 名字复用只对发布与真实存档中出现过的旧名有风险（开发构建与重构前快照里的同一实体可以复用自己的名字）。
        if (!legacy.Equals(target, StringComparison.OrdinalIgnoreCase) && requiredLegacyTypes.Contains(target) && !publicTypes.Contains(target))
        {
            Failures.Add((entry.ToString(), $"Name reuse: {target} is also the name of another published legacy type; the null-gated runtime fallbacks cannot tell them apart."));
        }
    }

    // ---- 标签 ----

    private Verdict Label(string owner, string label, CorpusRow? shape)
    {
        string? newOwner = MapType(owner);
        if (newOwner is null)
        {
            return typeMap.TryGetValue(owner, out ManifestEntry? ownerEntry)
                ? Verdict.Dropped("owner dropped", ownerEntry.ToString(), ownerEntry.Module)
                : Consumption("type:" + owner) is { } ownerStep ? Verdict.Consumed(ownerStep) : Verdict.Dropped("MooGirl era", "moogirlEra.typePrefixes", null);
        }

        // ModSettings 同时出现在 labels 与 settings 语料中；它的标签属于独立设置文件，
        // 必须使用 settings 行，不能要求在游戏存档表中重复登记一份并不会执行的标签映射。
        if (newOwner == build.SettingsClass)
        {
            Verdict setting = Setting(owner, label, shape?[5] ?? "-", mooGirlOnly: false);
            ManifestEntry? mapping = manifest?.SettingsLabels.FirstOrDefault(e => e.Old == label);
            if (mapping?.New is { } target)
            {
                CheckLabelLeftover(newOwner, label, "settings.labels");
                if (shape is not null && build.LabelsOf(newOwner, target) is { Count: > 0 } currentSettings)
                {
                    Verdict shapeVerdict = CompareShape(owner, label, shape, currentSettings);
                    if (shapeVerdict.Disposition == "undisposed")
                    {
                        return shapeVerdict;
                    }
                }
            }

            return setting;
        }

        if (labelMap.TryGetValue((owner, label), out ManifestEntry? entry))
        {
            if (entry.Disposition == "drop")
            {
                CheckLabelLeftover(newOwner, label, entry.ToString());
                return Verdict.Dropped(entry.Policy ?? "dropped", entry.ToString(), entry.Module);
            }

            if (entry.Disposition == "consumed")
            {
                CheckLabelLeftover(newOwner, label, entry.ToString());
                return entry.Step is not null && steps.ContainsKey(entry.Step) ? Verdict.Consumed(entry.Step, entry.Module) : Verdict.Undisposed($"consumed by undeclared step '{entry.Step}'");
            }

            string target = entry.New ?? "";
            if (build.LabelsOf(newOwner, target).Count == 0)
            {
                Failures.Add((entry.ToString(), $"Renamed to '{target}', which {newOwner} never reads or writes."));
            }

            if (label != target)
            {
                CheckLabelLeftover(newOwner, label, entry.ToString());
                if (legacyLabels.Contains((owner, target)))
                {
                    Failures.Add((entry.ToString(), $"Name reuse: '{target}' is another legacy label of {owner}."));
                }
            }

            return Verdict.Renamed(target, entry.ToString(), entry.Module);
        }

        if (consumedLabels.TryGetValue((owner, label), out (string Step, string? Problem) claim) && claim.Problem is null)
        {
            CheckLabelLeftover(newOwner, label, "step " + claim.Step);
            return Verdict.Consumed(claim.Step);
        }

        IReadOnlyList<LabelFact> current = build.LabelsOf(newOwner, label);
        if (current.Count == 0)
        {
            if (!build.Types.ContainsKey(newOwner) && Resolver.ConcreteDescendantsOfAbstract(owner) is { Count: > 0 } descendants)
            {
                foreach (string descendant in descendants)
                {
                    string? mapped = MapType(descendant);
                    IReadOnlyList<LabelFact> inherited = mapped is null ? [] : build.LabelsOf(mapped, label);
                    if (inherited.Count == 0)
                    {
                        return Verdict.Undisposed($"removed abstract owner {owner}: concrete descendant {descendant} no longer preserves '{label}'");
                    }
                    if (shape is not null && CompareShape(owner, label, shape, inherited) is { Disposition: "undisposed" } mismatch)
                    {
                        return Verdict.Undisposed($"removed abstract owner {owner}: concrete descendant {descendant}: {mismatch.Detail}");
                    }
                }
                return Verdict.Unchanged("abstract declaration merged; every concrete descendant preserves the label and Scribe shape");
            }
            return Verdict.Undisposed(build.Types.ContainsKey(newOwner) ? $"{newOwner} no longer reads or writes '{label}'" : $"owner {newOwner} does not exist in the new build");
        }

        if (newOwner == owner)
        {
            StillPresentLabel(owner, label);
        }

        return shape is null ? Verdict.Unchanged() : CompareShape(owner, label, shape, current);
    }

    private void StillPresentLabel(string owner, string label)
    {
        StillPresent.Add(LegacyCorpus.LabelsFile + "|" + owner + "|" + label);
        StillPresent.Add(LegacyCorpus.ObservedFile + "|label|" + label + "|" + owner);
    }

    private Verdict CompareShape(string owner, string label, CorpusRow row, IReadOnlyList<LabelFact> current)
    {
        string kind = row[2];
        string valueType = MapTypeNames(row[3]);
        List<LabelFact> sameKind = current.Where(f => f.Kind == kind).ToList();
        if (sameKind.Count == 0)
        {
            return Verdict.Undisposed($"Scribe kind changed from {kind} to {string.Join("/", current.Select(f => f.Kind).Distinct())}");
        }

        if (!sameKind.Any(f => SameType(valueType, f.ValueType)))
        {
            return Verdict.Undisposed($"value type changed from {row[3]} to {sameKind[0].ValueType}");
        }

        if (!sameKind.Any(f => SameLookMode(row[4], f.LookMode)))
        {
            return Verdict.Undisposed($"LookMode changed from {row[4]} to {sameKind[0].LookMode}");
        }

        // forceSave 的旧标签总是写进存档：缺失的节点不会按旧默认值解释，默认值变化无害。新默认值不是常量（取自 Def、静态属性）时
        // 无法离线比较，由 B.scribe-defaults 要求 forceSave。
        if (kind == "Values" && IsConstant(row[5]) && row[6] != "True" && sameKind.All(f => IsConstant(f.Default) && f.Default != row[5]))
        {
            if (materialized.Contains((owner, label, row[5])))
            {
                return Verdict.Unchanged("defaults (old default materialised)");
            }

            return Verdict.Undisposed($"default drift: legacy saves omitted the node when the value was {row[5]}, the new build loads a missing node as {sameKind[0].Default}");
        }

        return Verdict.Unchanged();
    }

    private void CheckLabelLeftover(string newOwner, string label, string via)
    {
        if (build.OwnLabels(newOwner).Contains(label, StringComparer.Ordinal))
        {
            Failures.Add(($"{newOwner}/{label}", $"Legacy label still read by domain code although {via} replaces it."));
        }
    }

    // 值类型里的本 Mod 类型名按类型映射替换。
    private string MapTypeNames(string valueType)
        => TypeToken().Replace(valueType, match => typeMap.TryGetValue(match.Value, out ManifestEntry? entry) && entry.New is { } renamed ? renamed : match.Value);

    // 泛型辅助方法中的类型形参（T）与任何类型兼容。
    private static bool SameType(string legacy, string current)
        => legacy == current || IsGenericParameter(legacy) || IsGenericParameter(current);

    private static bool IsGenericParameter(string type) => type.Length > 0 && !type.Contains('.', StringComparison.Ordinal) && !type.Contains('<', StringComparison.Ordinal);

    // LookMode 逐项比较（字典为 键=>值）；Undefined 由元素类型决定（IExposable 即 Deep），与任何具体模式兼容。
    private static bool SameLookMode(string legacy, string current)
    {
        if (legacy == current || legacy == "dynamic" || current == "dynamic")
        {
            return true;
        }

        string[] a = legacy.Split("=>");
        string[] b = current.Split("=>");
        return a.Length == b.Length && a.Zip(b).All(pair => pair.First == pair.Second || pair.First is "Undefined" or "dynamic" || pair.Second is "Undefined" or "dynamic");
    }

    // 默认值文本是编译期常量（"static Type::get_X" 是静态属性，值无法离线比较）。
    private static bool IsConstant(string value) => value is not ("-" or "dynamic" or "def-dependent") && !value.StartsWith("static ", StringComparison.Ordinal);

    private string? Consumption(string token)
        => consumed.TryGetValue(token, out (string Step, string? Problem) claim) && claim.Problem is null ? claim.Step : null;

    private static string Unquote(string value)
        => value.Length >= 2 && value[0] == '"' ? System.Text.Json.JsonSerializer.Deserialize<string>(value) ?? value : value;

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_`]*)+(\+[A-Za-z_][A-Za-z0-9_`]*)*", RegexOptions.CultureInvariant)]
    private static partial Regex TypeToken();
}
