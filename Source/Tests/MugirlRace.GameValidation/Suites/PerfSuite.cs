using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using HarmonyLib;
using Mugirl.GameValidation.Core;
using Verse.AI;
using Waylith.Profiling;
using Waylith.Profiling.Instrumentation;

namespace Mugirl.GameValidation.Suites;

/// <summary>
/// 性能测量：同一张测试地图上对比雪牛娘与人类殖民者，并触发一次雪牛娘迁徙。只按名运行，不在 default 与 all 中。
/// </summary>
/// <remarks>
/// <para>步骤：</para>
/// <list type="number">
/// <item>setup：生成雪牛娘与人类殖民者，按需补种野草，触发迁徙事件，记录地图上植物与食物源的数量。</item>
/// <item>food-search：以 JobGiver_GetFood 的参数调用原版觅食搜索，按饥饿程度、食物方案与有无现成饭菜分场景计时；
/// 同时调用 BestFoodSourceOnMap 的未补丁原版副本（Harmony 反向补丁），选中的食物必须与本进程中实际的方法相同（唯一的通过/失败判定）。
/// 场景结束后默认收回放下的饭菜。</item>
/// <item>tick-cost：预热 60 tick 后，按种族与阵营统计地图上 Pawn 顶层 Thing.DoTick 的耗时、每 tick 合计与尖峰 tick，
/// 口径与 Dubs Performance Analyzer 的“物体 Tick / Tick Def”相同（尖峰：超过 1 ms 且超过中位数 5 倍的 tick）。</item>
/// <item>profiler：开启游戏内置帧分析器（Waylith FrameProfiler）及其插桩，游戏保持暂停、每帧单步一个 tick，按种族给 DoTick 套区段，
/// 导出各组下调用路径的耗时、调用次数与分配，以及该组耗时最高的几个 tick 的组成。</item>
/// </list>
/// <para>
/// 耗时只报告（报告中的 info 行与结果目录中的 perf.json），不按阈值判定：结果随机器、地图与生成的 Pawn 变化。
/// 插桩会改写被测方法体并为每次调用记录区段，插桩阶段的绝对耗时偏高，只用于比较构成；不插桩的 tick-cost 才是实际开销。
/// </para>
/// <para>
/// 参数（<see cref="ValidationOptions.SuiteOptions"/>）：perf.mugirls（默认 4）、perf.humans（4）、perf.plants（补种野草后的植物总数下限，
/// 默认 0 不补种）、perf.repeats（5）、perf.meals（觅食场景放下的简易套餐份数，20）、perf.keepMeals（false：场景后收回）、
/// perf.ticks（tick-cost 的 tick 数，2500）、perf.hunger（tick 阶段开始时殖民者的饥饿度，默认 0.26，none 不设置）、
/// perf.migration（true）、perf.profile（true）、perf.profileTicks（1500）、perf.alloc（true）、
/// perf.targets（分号分隔的插桩目标，默认雪牛娘与原版 Verse/RimWorld）。
/// </para>
/// </remarks>
internal static class PerfSuite
{
    public const string Name = "perf";

    private const string ResultFile = "perf.json";
    private const string HumanRolePrefix = "human-";
    private const string MigrationIncident = "Mugirl_Migration";
    private const string DefaultTargets = "MugirlRace!*;Assembly-CSharp!Verse.*::*;Assembly-CSharp!RimWorld.*::*";
    private const string ProbeLabelPrefix = "Perf:DoTick ";
    private const int TopPaths = 30;
    private const int WorstFramesPerGroup = 3;
    private const int ProfileWarmupTicks = 60;
    private const int StopwatchWarmupTicks = 60;

    // StaticCacheLifecycle: 本套件一次运行内的结果与迁徙成员；Setup 步骤开头重置。
    private static JsonObject result = new();
    private static readonly List<int> WildMugirls = [];

    public static ValidationSuite Create() => new(
        Name,
        "performance measurements: Mugirl vs human food search, per-race DoTick cost and spikes, built-in frame profiler call paths (report only, no timing thresholds)",
        SuiteNeeds.MugirlMod,
        [
            new ValidationStep("setup", Setup),
            new ValidationStep("food-search", FoodSearch, TimeoutSeconds: 600),
            new ValidationStep("tick-cost", TickCost, TimeoutSeconds: 900),
            new ValidationStep("profiler", Profile, TimeoutSeconds: 1200),
        ],
        inDefaultRun: false)
    {
        InAllRun = false,
    };

    private static IEnumerable<Wait> Setup(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        result = new JsonObject { ["suite"] = Name, ["game"] = VersionControl.CurrentVersionStringWithRev };
        WildMugirls.Clear();
        if (ctx.Target?.Assembly?.Location is { Length: > 0 } dll && File.Exists(dll))
        {
            result["mugirlDll"] = new JsonObject { ["path"] = dll, ["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))) };
        }

        Map map = ctx.Map;
        List<Pawn> mugirls = GameFixtures.EnsureMugirlColonists(ctx, ctx.Options.SuiteOption(Name + ".mugirls", 4));
        List<Pawn> humans = EnsureHumanColonists(ctx, ctx.Options.SuiteOption(Name + ".humans", 4));
        int added = TopUpWildGrass(map, ctx.Options.SuiteOption(Name + ".plants", 0));
        bool fired = false;
        if (ctx.Options.SuiteOption(Name + ".migration", "true") == "true")
        {
            fired = TryFireMigration(ctx, map);
        }

        var pawns = new JsonObject
        {
            ["mugirlColonists"] = new JsonArray([.. mugirls.Select(p => (JsonNode)p.ThingID)]),
            ["humanColonists"] = new JsonArray([.. humans.Select(p => (JsonNode)p.ThingID)]),
            ["migrationFired"] = fired,
            ["wildMugirls"] = WildMugirls.Count,
        };
        result["pawns"] = pawns;
        result["map"] = MapFacts(map, added);
        report.Info($"map {map.Size.x}x{map.Size.z} {map.Biome?.defName}: {Count(map, ThingRequestGroup.Plant)} plants, "
            + $"{Count(map, ThingRequestGroup.FoodSource)} food sources ({Count(map, ThingRequestGroup.FoodSourceNotPlantOrTree)} excluding plants/trees), {added} grass added");
        report.Info($"{mugirls.Count} Mugirl colonists, {humans.Count} human colonists, migration fired {fired} ({WildMugirls.Count} wild Mugirls)");
        report.Check(mugirls.Count > 0 && humans.Count > 0, "test colonists exist", () => $"{mugirls.Count} Mugirls, {humans.Count} humans");
        WriteResult(ctx);
        yield return Wait.Frames(2);
    }

    private static IEnumerable<Wait> FoodSearch(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        int repeats = Math.Max(1, ctx.Options.SuiteOption(Name + ".repeats", 5));
        VanillaFood.Prepare(ctx);
        var scenarios = new JsonArray();
        var mismatches = new List<string>();
        RunFoodScenarios(ctx, scenarios, mismatches, string.Empty, repeats);
        List<Thing> placed = PlaceMeals(ctx.Map, ctx.Options.SuiteOption(Name + ".meals", 20));
        int meals = placed.Sum(t => t.stackCount);
        report.Info($"placed {meals} simple meals near the map centre");
        if (meals > 0)
        {
            RunFoodScenarios(ctx, scenarios, mismatches, "/meals", repeats);
        }

        // 默认收回放下的套餐：后面的 tick 阶段测的是没有现成饭菜、只能吃草的殖民地（雪牛娘最常见的重复觅食情形）。
        if (ctx.Options.SuiteOption(Name + ".keepMeals", "false") != "true")
        {
            foreach (Thing thing in placed.Where(t => t.Spawned && !t.Destroyed))
            {
                thing.Destroy(DestroyMode.Vanish);
            }
        }

        report.Check(scenarios.Count > 0, "food search scenarios ran", () => "no test pawn was available");
        report.Check(mismatches.Count == 0, "BestFoodSourceOnMap picks the same food as an unpatched vanilla copy in every scenario",
            () => string.Join("; ", mismatches));
        result["foodSearch"] = scenarios;
        WriteResult(ctx);
        yield return Wait.Frames(1);
    }

    private static void RunFoodScenarios(ValidationContext ctx, JsonArray scenarios, List<string> mismatches, string suffix, int repeats)
    {
        ValidationReport report = ctx.Report;
        Pawn? mugirl = FirstAlive(ctx, GameFixtures.RoleName(0));
        Pawn? human = FirstAlive(ctx, HumanRolePrefix + 1);
        Pawn? wild = WildMugirls.Select(ValidationContext.FindPawn).FirstOrDefault(p => p is { Spawned: true, Dead: false });
        FoodPolicy? grass = FindGrassPolicy();
        var runs = new List<(string Name, Pawn Pawn, float Level, FoodPolicy? Policy)>();
        if (mugirl is not null)
        {
            runs.Add(("mugirl-colonist/hungry", mugirl, HungryLevel(mugirl), null));
            if (grass is not null)
            {
                runs.Add(("mugirl-colonist/hungry/grass-policy", mugirl, HungryLevel(mugirl), grass));
            }
            else
            {
                report.Skip("grass policy scenario" + suffix, "no food policy labelled 草/Grass exists");
            }

            runs.Add(("mugirl-colonist/urgently-hungry", mugirl, UrgentLevel(mugirl), null));
            runs.Add(("mugirl-colonist/starving", mugirl, 0f, null));
        }

        if (human is not null)
        {
            runs.Add(("human-colonist/hungry", human, HungryLevel(human), null));
            runs.Add(("human-colonist/urgently-hungry", human, UrgentLevel(human), null));
        }

        if (wild is not null)
        {
            runs.Add(("mugirl-wild/hungry", wild, HungryLevel(wild), null));
        }
        else
        {
            report.Skip("wild Mugirl scenario" + suffix, "no migration pawn is on the map");
        }

        foreach ((string name, Pawn pawn, float level, FoodPolicy? policy) in runs)
        {
            JsonObject measured = MeasureFoodSearch(report, name + suffix, pawn, level, policy, repeats);
            scenarios.Add(measured);
            if (measured["sameAsVanilla"]?.GetValue<bool>() != true)
            {
                mismatches.Add($"{name + suffix}: live {measured["onMap"]?["result"]}, vanilla {measured["vanilla"]?["result"]}");
            }
        }
    }

    /// <summary>在地图中心附近放置未禁用的简易套餐（每堆 10 份），返回放下的物品。</summary>
    private static List<Thing> PlaceMeals(Map map, int count)
    {
        var placed = new List<Thing>();
        int total = 0;
        while (total < count)
        {
            Thing meals = ThingMaker.MakeThing(ThingDefOf.MealSimple);
            meals.stackCount = Math.Min(10, count - total);
            total += meals.stackCount;
            if (!GenPlace.TryPlaceThing(meals, GameFixtures.FreeCellNear(map, map.Center, 6), map, ThingPlaceMode.Near, out Thing? result))
            {
                break;
            }

            result.SetForbidden(false, warnOnFail: false);
            placed.Add(result);
        }

        return placed;
    }

    private static IEnumerable<Wait> TickCost(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        int ticks = Math.Max(250, ctx.Options.SuiteOption(Name + ".ticks", 2500));
        int hungry = SetColonistHunger(ctx);
        // 预热：所有殖民者同时被设为饥饿后的头几个 tick 会集中决策并首次编译相关代码，计量从预热之后开始。
        yield return Wait.Ticks(StopwatchWarmupTicks);
        DoTickProbe.Install(ctx);
        DoTickProbe.BeginStopwatch(Find.TickManager.TicksGame, ticks);
        try
        {
            yield return Wait.Ticks(ticks, timeoutSeconds: 840);
        }
        finally
        {
            DoTickProbe.End();
        }

        JsonArray groups = DoTickProbe.StopwatchSummary(report);
        result["tickCost"] = new JsonObject { ["ticks"] = ticks, ["hungryColonists"] = hungry, ["groups"] = groups };
        report.Check(groups.Count > 0, "DoTick of pawns was measured", () => "the DoTick probe saw no pawn");
        WriteResult(ctx);
    }

    private static IEnumerable<Wait> Profile(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        if (ctx.Options.SuiteOption(Name + ".profile", "true") != "true")
        {
            report.Skip("built-in profiler", "perf.profile is not true");
            yield break;
        }

        int ticks = Math.Max(100, ctx.Options.SuiteOption(Name + ".profileTicks", 1500));
        bool alloc = ctx.Options.SuiteOption(Name + ".alloc", "true") == "true";
        string[] targets = ctx.Options.SuiteOption(Name + ".targets", DefaultTargets)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var state = ProfilerState.Capture();
        var profile = new JsonObject();
        result["profiler"] = profile;
        int hungry = SetColonistHunger(ctx);
        int startTick = Find.TickManager.TicksGame;
        try
        {
            FrameProfiler.SetHistoryCapacity(10000);
            FrameProfiler.SetEnabled(true);
            FrameProfiler.SetPaused(false);
            FrameProfiler.SetMainThreadOnly(true);
            if (alloc && FrameProfiler.AllocationTrackingSupported)
            {
                FrameProfiler.SetAllocationTracking(true);
            }

            ProfilerInstrumentation.SetTargets(targets);
            var instrumentClock = Stopwatch.StartNew();
            if (ProfilerInstrumentation.IsSupported)
            {
                ProfilerInstrumentation.SetEnabled(true);
            }

            profile["instrumentation"] = new JsonObject
            {
                ["supported"] = ProfilerInstrumentation.IsSupported,
                ["enabled"] = ProfilerInstrumentation.IsEnabled,
                ["targets"] = new JsonArray([.. targets.Select(t => (JsonNode)t)]),
                ["matched"] = ProfilerInstrumentation.MatchedMethodCount,
                ["instrumented"] = ProfilerInstrumentation.InstrumentedMethodCount,
                ["seconds"] = Math.Round(instrumentClock.Elapsed.TotalSeconds, 2),
                ["allocTracking"] = FrameProfiler.AllocationTracking,
                ["scopeOverheadNs"] = Math.Round(FrameProfiler.ScopeOverheadNanoseconds, 1),
            };
            report.Info($"built-in profiler on: instrumentation supported {ProfilerInstrumentation.IsSupported}, "
                + $"{ProfilerInstrumentation.InstrumentedMethodCount} of {ProfilerInstrumentation.MatchedMethodCount} matched methods instrumented "
                + $"in {instrumentClock.Elapsed.TotalSeconds:0.0} s, allocation tracking {FrameProfiler.AllocationTracking}");
            DoTickProbe.Install(ctx);
            DoTickProbe.BeginScopes();

            // 预热：插桩后的方法第一次调用要重新编译，先单步若干 tick，测量窗口从预热之后开始。
            for (int i = 0; i < ProfileWarmupTicks; i++)
            {
                Find.TickManager.DoSingleTick();
                yield return Wait.Frames(0);
            }

            // 游戏保持暂停，每帧只单步一个 tick（与原版时间控件的单步相同）：帧快照与 tick 一一对应，可以逐 tick 找出尖峰；
            // 高速运行时一帧包含多个 tick，全量插桩的区段事件会超出分析器的事件缓冲。
            DoTickProbe.BeginScopes();
            int startOrdinal = ProfileReader.NewestOrdinal();
            startTick = Find.TickManager.TicksGame;
            for (int i = 0; i < ticks; i++)
            {
                Find.TickManager.DoSingleTick();
                yield return Wait.Frames(0);
            }

            DoTickProbe.End();
            int endTick = Find.TickManager.TicksGame;
            // 帧快照由分析器的后台线程在帧结束后发布；多等几帧再暂停，确保覆盖最后的 tick。
            yield return Wait.Frames(15);
            FrameProfiler.SetPaused(true);
            ProfileReader.Read(report, profile, startTick, endTick, startOrdinal, hungry);
        }
        finally
        {
            DoTickProbe.End();
            state.Restore();
        }

        WriteResult(ctx);
    }

    /// <summary>
    /// 一个觅食场景：设好饥饿度与食物方案后，分别计时 JobGiver_GetFood 的完整决策、本进程中实际的 BestFoodSourceOnMap
    /// （含所有 Harmony 补丁），以及它的未补丁原版副本；后两者参数与 JobGiver_GetFood 经 TryFindBestFoodSourceFor 传入的相同，
    /// 选中的食物必须一致。
    /// </summary>
    private static JsonObject MeasureFoodSearch(ValidationReport report, string scenario, Pawn pawn, float level, FoodPolicy? policy, int repeats)
    {
        Need_Food food = pawn.needs.food;
        float previousLevel = food.CurLevelPercentage;
        FoodPolicy? previousPolicy = pawn.foodRestriction?.CurrentFoodPolicy;
        var giver = new JobGiver_GetFood();
        var giverTimes = new Timings(repeats);
        var liveTimes = new Timings(repeats);
        var vanillaTimes = new Timings(repeats);
        string job = "-";
        Thing? live = null;
        Thing? vanilla = null;
        ThingDef? liveDef = null;
        ThingDef? vanillaDef = null;
        HungerCategory category;
        try
        {
            food.CurLevelPercentage = level;
            category = food.CurCategory;
            if (policy is not null && pawn.foodRestriction is { } restriction)
            {
                restriction.CurrentFoodPolicy = policy;
            }

            bool desperate = category == HungerCategory.Starving;
            bool allowCorpse = pawn.AnimalOrWildMan() || (pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Malnutrition)?.Severity ?? 0f) > 0.4f;
            bool allowDrug = !pawn.IsTeetotaler();
            bool allowHarvest = pawn.IsWildMan();

            // 第一次调用包含 JIT 与可达性缓存填充，单独记录；之后三种调用交替进行，避免先后顺序带来的缓存偏差。
            for (int i = 0; i <= repeats; i++)
            {
                long start = Stopwatch.GetTimestamp();
                ThinkResult think = giver.TryIssueJobPackage(pawn, default);
                giverTimes.Add(i, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                job = think.Job is { } issued ? $"{issued.def.defName} {issued.targetA.Thing?.def.defName ?? issued.targetA.Cell.ToString()}" : "no job";

                start = Stopwatch.GetTimestamp();
                live = FoodUtility.BestFoodSourceOnMap(pawn, pawn, desperate, out liveDef, FoodPreferability.MealLavish, allowPlant: true, allowDrug, allowCorpse,
                    allowDispenserFull: true, allowDispenserEmpty: true, allowForbidden: false, allowSociallyImproper: false, allowHarvest);
                liveTimes.Add(i, Stopwatch.GetElapsedTime(start).TotalMilliseconds);

                start = Stopwatch.GetTimestamp();
                vanilla = VanillaFood.BestFoodSourceOnMap(pawn, pawn, desperate, out vanillaDef, FoodPreferability.MealLavish, true, allowDrug, allowCorpse,
                    true, true, false, false, allowHarvest, false, false, false, FoodPreferability.Undefined, null, false);
                vanillaTimes.Add(i, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
        }
        finally
        {
            food.CurLevelPercentage = previousLevel;
            if (policy is not null && pawn.foodRestriction is { } restriction)
            {
                restriction.CurrentFoodPolicy = previousPolicy;
            }
        }

        bool same = live == vanilla && liveDef == vanillaDef;
        Map map = pawn.Map;
        report.Info($"{scenario}: {pawn.def.defName} {pawn.ThingID} food {level:0.000} ({category}); JobGiver_GetFood median {giverTimes.Median:0.000} ms "
            + $"(max {giverTimes.Max:0.000}, first {giverTimes.First:0.000}) -> {job}; BestFoodSourceOnMap {liveTimes.Median:0.000} ms, "
            + $"unpatched vanilla {vanillaTimes.Median:0.000} ms, same result {same} ({Describe(live, liveDef)})");
        return new JsonObject
        {
            ["scenario"] = scenario,
            ["pawn"] = pawn.ThingID,
            ["race"] = pawn.def.defName,
            ["faction"] = pawn.Faction?.def.defName ?? "none",
            ["level"] = Math.Round(level, 4),
            ["category"] = category.ToString(),
            ["policy"] = policy?.label ?? previousPolicy?.label ?? "-",
            ["foodTypes"] = pawn.RaceProps.foodType.ToString(),
            ["foodSources"] = Count(map, ThingRequestGroup.FoodSource),
            ["foodSourcesExcludingPlants"] = Count(map, ThingRequestGroup.FoodSourceNotPlantOrTree),
            ["jobGiver"] = giverTimes.ToJson(job),
            ["onMap"] = liveTimes.ToJson(Describe(live, liveDef)),
            ["vanilla"] = vanillaTimes.ToJson(Describe(vanilla, vanillaDef)),
            ["sameAsVanilla"] = same,
        };
    }

    private static string Describe(Thing? thing, ThingDef? def)
        => thing is null ? "none" : $"{thing.ThingID} at {thing.PositionHeld} as {def?.defName ?? "-"}";

    /// <summary>一组计时：第 0 次单独记为首次调用，其余求中位数与极值。</summary>
    private sealed class Timings(int capacity)
    {
        private readonly List<double> samples = new(capacity);

        public double First { get; private set; }

        public double Median => Sorted()[samples.Count / 2];

        public double Max => samples.Count == 0 ? 0 : samples.Max();

        public void Add(int index, double ms)
        {
            if (index == 0)
            {
                First = ms;
            }
            else
            {
                samples.Add(ms);
            }
        }

        public JsonObject ToJson(string resultText)
        {
            List<double> sorted = Sorted();
            return new JsonObject
            {
                ["firstMs"] = Math.Round(First, 4),
                ["medianMs"] = Math.Round(sorted[sorted.Count / 2], 4),
                ["minMs"] = Math.Round(sorted[0], 4),
                ["maxMs"] = Math.Round(sorted[^1], 4),
                ["result"] = resultText,
            };
        }

        private List<double> Sorted()
        {
            List<double> sorted = [.. samples];
            sorted.Sort();
            return sorted;
        }
    }

    /// <summary>
    /// FoodUtility.BestFoodSourceOnMap 的未补丁原版副本（Harmony 反向补丁），用来与本进程中实际的方法比较选中的食物与耗时。
    /// </summary>
    private static class VanillaFood
    {
        // StaticCacheLifecycle: 进程级；反向补丁只需建立一次，副本不随运行结束的 UnpatchAll 改变。
        private static bool prepared;

        public static void Prepare(ValidationContext ctx)
        {
            if (prepared)
            {
                return;
            }

            MethodInfo original = AccessTools.Method(typeof(FoodUtility), nameof(FoodUtility.BestFoodSourceOnMap));
            ctx.Harmony.CreateReversePatcher(original, new HarmonyMethod(typeof(VanillaFood), nameof(BestFoodSourceOnMap))).Patch();
            prepared = true;
        }

        // 方法体由反向补丁替换为原版 IL；签名与原方法逐项相同。
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Thing? BestFoodSourceOnMap(Pawn getter, Pawn eater, bool desperate, out ThingDef? foodDef, FoodPreferability maxPref, bool allowPlant,
            bool allowDrug, bool allowCorpse, bool allowDispenserFull, bool allowDispenserEmpty, bool allowForbidden, bool allowSociallyImproper,
            bool allowHarvest, bool forceScanWholeMap, bool ignoreReservations, bool calculateWantedStackCount, FoodPreferability minPrefOverride,
            float? minNutrition, bool allowVenerated)
            => throw new NotImplementedException("replaced by a Harmony reverse patch of FoodUtility.BestFoodSourceOnMap");
    }

    private static List<Pawn> EnsureHumanColonists(ValidationContext ctx, int count)
    {
        Map map = ctx.Map;
        var pawns = new List<Pawn>(count);
        for (int i = 0; i < count; i++)
        {
            string role = HumanRolePrefix + (i + 1);
            if (ctx.TryGetPawn(role, out Pawn? existing) && existing.Spawned && existing.Map == map && !existing.Dead)
            {
                pawns.Add(existing);
                continue;
            }

            var request = new PawnGenerationRequest(
                PawnKindDefOf.Colonist,
                Faction.OfPlayer,
                PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true,
                canGeneratePawnRelations: false,
                developmentalStages: DevelopmentalStage.Adult);
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            GenSpawn.Spawn(pawn, GameFixtures.FreeCellNear(map, map.Center, 10), map);
            ctx.SetRole(role, pawn);
            pawns.Add(pawn);
        }

        return pawns;
    }

    /// <summary>在可种植的空地上补种成熟野草，直到植物总数达到 <paramref name="wanted"/>；返回补种的株数。</summary>
    private static int TopUpWildGrass(Map map, int wanted)
    {
        int existing = Count(map, ThingRequestGroup.Plant);
        if (wanted <= existing)
        {
            return 0;
        }

        ThingDef grass = ThingDefOf.Plant_Grass;
        int added = 0;
        foreach (IntVec3 cell in map.AllCells.InRandomOrder())
        {
            if (existing + added >= wanted)
            {
                break;
            }

            if (map.fertilityGrid.FertilityAt(cell) < grass.plant.fertilityMin || !cell.Standable(map) || cell.GetPlant(map) is not null
                || cell.GetEdifice(map) is not null || cell.GetFirstItem(map) is not null || cell.GetFirstPawn(map) is not null)
            {
                continue;
            }

            var plant = (Plant)ThingMaker.MakeThing(grass);
            plant.Growth = 1f;
            GenSpawn.Spawn(plant, cell, map);
            added++;
        }

        return added;
    }

    private static bool TryFireMigration(ValidationContext ctx, Map map)
    {
        IncidentDef? incident = DefDatabase<IncidentDef>.GetNamedSilentFail(MigrationIncident);
        if (incident is null)
        {
            ctx.Report.Skip("migration", MigrationIncident + " is not defined");
            return false;
        }

        HashSet<Pawn> before = [.. map.mapPawns.AllPawnsSpawned];
        IncidentParms parms = StorytellerUtility.DefaultParmsNow(incident.category, map);
        parms.forced = true;
        bool fired = incident.Worker.TryExecute(parms);
        foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
        {
            if (!before.Contains(pawn) && pawn.def == GameFixtures.MugirlRace && pawn.Faction is null)
            {
                WildMugirls.Add(pawn.thingIDNumber);
            }
        }

        if (!fired)
        {
            ctx.Report.Skip("migration", "the incident worker declined to fire on this map");
        }

        return fired;
    }

    /// <summary>tick 阶段开始时把所有殖民者设为同样的饥饿度（高于饥饿档、低于想吃东西的阈值），让两个种族在测量期间都去觅食。</summary>
    private static int SetColonistHunger(ValidationContext ctx)
    {
        string option = ctx.Options.SuiteOption(Name + ".hunger", "0.26");
        if (option == "none" || !float.TryParse(option, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float level))
        {
            return 0;
        }

        int count = 0;
        foreach (Pawn pawn in ctx.Map.mapPawns.FreeColonistsSpawned)
        {
            if (pawn.needs?.food is { } food)
            {
                food.CurLevelPercentage = level;
                count++;
            }
        }

        return count;
    }

    private static float HungryLevel(Pawn pawn)
    {
        Need_Food food = pawn.needs.food;
        return (food.PercentageThreshHungry + food.PercentageThreshUrgentlyHungry) / 2f;
    }

    private static float UrgentLevel(Pawn pawn) => pawn.needs.food.PercentageThreshUrgentlyHungry / 2f;

    private static FoodPolicy? FindGrassPolicy()
        => Current.Game.foodRestrictionDatabase?.AllFoodRestrictions.FirstOrDefault(p => p?.label is "草" or "Grass");

    private static Pawn? FirstAlive(ValidationContext ctx, string role)
        => ctx.TryGetPawn(role, out Pawn? pawn) && pawn.Spawned && !pawn.Dead ? pawn : null;

    private static int Count(Map map, ThingRequestGroup group) => map.listerThings.ThingsInGroup(group).Count;

    private static JsonObject MapFacts(Map map, int added) => new()
    {
        ["size"] = $"{map.Size.x}x{map.Size.z}",
        ["biome"] = map.Biome?.defName ?? "-",
        ["plants"] = Count(map, ThingRequestGroup.Plant),
        ["harvestablePlants"] = Count(map, ThingRequestGroup.HarvestablePlant),
        ["foodSources"] = Count(map, ThingRequestGroup.FoodSource),
        ["foodSourcesExcludingPlants"] = Count(map, ThingRequestGroup.FoodSourceNotPlantOrTree),
        ["grassAdded"] = added,
        ["spawnedPawns"] = map.mapPawns.AllPawnsSpawnedCount,
        ["plantOnlyFoodCeiling"] = PlantOnlyFoodCeiling().ToString(),
    };

    /// <summary>原版 FoodSource 组比 FoodSourceNotPlantOrTree 组多出的食物 Def 中的最高可口度（与被测 Mod 的判定独立计算，供核对）。</summary>
    private static FoodPreferability PlantOnlyFoodCeiling()
    {
        FoodPreferability ceiling = FoodPreferability.Undefined;
        foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            if (ThingRequestGroup.FoodSource.Includes(def) && !ThingRequestGroup.FoodSourceNotPlantOrTree.Includes(def)
                && def.ingestible is { } ingestible && ingestible.preferability > ceiling)
            {
                ceiling = ingestible.preferability;
            }
        }

        return ceiling;
    }

    private static void WriteResult(ValidationContext ctx)
    {
        if (ctx.ArtifactPath(ResultFile) is { } path)
        {
            ArtifactJson.Write(path, result);
        }
    }

    /// <summary>按种族与阵营归组的 Pawn 名，例如 Mugirl、Mugirl(wild)、Human。</summary>
    private static string GroupOf(Pawn pawn)
    {
        Faction? faction = pawn.Faction;
        string suffix = faction is null ? "(wild)" : faction.IsPlayer ? string.Empty : "(" + faction.def.defName + ")";
        return pawn.def.defName + suffix;
    }

    /// <summary>
    /// Thing.DoTick 的测量探针：秒表模式按组累计顶层 Pawn 的耗时；区段模式给顶层 Pawn 的 DoTick 套上内置分析器的区段。
    /// 嵌套的 DoTick（背包、装备、搭载的 Pawn）计入外层，与 Dubs Performance Analyzer 只测 TickList 调用点的口径一致。
    /// </summary>
    private static class DoTickProbe
    {
        private enum Mode
        {
            Off,
            Stopwatch,
            Scopes,
        }

        // StaticCacheLifecycle: 探针状态只在本套件的 tick 步骤内有效；补丁随运行结束的 UnpatchAll 撤销。
        private static bool installed;
        private static Mode mode;
        private static int depth;
        private static int firstTick;
        private static int tickSpan;
        private static readonly Dictionary<(ThingDef Def, Faction? Faction), Group> Groups = new();
        private static readonly Dictionary<string, Group> ByName = new(StringComparer.Ordinal);

        public static void Install(ValidationContext ctx)
        {
            if (installed)
            {
                return;
            }

            ctx.Harmony.Patch(
                AccessTools.Method(typeof(Thing), nameof(Thing.DoTick)),
                prefix: new HarmonyMethod(typeof(DoTickProbe), nameof(Prefix)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(DoTickProbe), nameof(Finalizer)) { priority = Priority.Last });
            installed = true;
        }

        public static void BeginStopwatch(int startTick, int ticks)
        {
            Reset();
            firstTick = startTick;
            tickSpan = ticks + 2;
            mode = Mode.Stopwatch;
        }

        public static void BeginScopes()
        {
            Reset();
            mode = Mode.Scopes;
        }

        public static void End()
        {
            mode = Mode.Off;
            depth = 0;
        }

        public static int ScopeCalls => ByName.Values.Sum(g => g.Calls);

        private static void Reset()
        {
            Groups.Clear();
            ByName.Clear();
            depth = 0;
        }

        public static void Prefix(Thing __instance, out long __state)
        {
            __state = 0;
            // 只测地图上的 Pawn；世界 Pawn 由 WorldPawns 推进，不在“物体 Tick”的口径内。
            if (mode == Mode.Off || __instance is not Pawn { Spawned: true } pawn)
            {
                return;
            }

            if (depth++ > 0)
            {
                __state = -1;
                return;
            }

            Group group = GroupFor(pawn);
            group.Calls++;
            if (mode == Mode.Stopwatch)
            {
                __state = Stopwatch.GetTimestamp();
            }
            else
            {
                __state = FrameProfiler.TryEnterScope(group.Label) ? -2 : -1;
            }

            current = group;
        }

        public static Exception? Finalizer(Exception? __exception, long __state)
        {
            if (__state == 0)
            {
                return __exception;
            }

            depth--;
            if (__state == -2)
            {
                FrameProfiler.LeaveScope();
            }
            else if (__state > 0 && current is { } group)
            {
                double ms = Stopwatch.GetElapsedTime(__state).TotalMilliseconds;
                group.TotalMs += ms;
                int index = Find.TickManager.TicksGame - firstTick;
                if ((uint)index < (uint)group.PerTick.Length)
                {
                    group.PerTick[index] += ms;
                }
            }

            return __exception;
        }

        // StaticCacheLifecycle: 当前顶层 DoTick 所属的组，只在一次顶层调用内有效。
        private static Group? current;

        private static Group GroupFor(Pawn pawn)
        {
            (ThingDef, Faction?) key = (pawn.def, pawn.Faction);
            if (!Groups.TryGetValue(key, out Group? group))
            {
                string name = GroupOf(pawn);
                if (!ByName.TryGetValue(name, out group))
                {
                    group = new Group(name, tickSpan);
                    ByName[name] = group;
                }

                Groups[key] = group;
            }

            group.Pawns.Add(pawn.thingIDNumber);
            return group;
        }

        public static JsonArray StopwatchSummary(ValidationReport report)
        {
            var groups = new JsonArray();
            foreach (Group group in ByName.Values.OrderByDescending(g => g.TotalMs))
            {
                double[] ticks = group.PerTick;
                double[] active = [.. ticks.Where(v => v > 0)];
                Array.Sort(active);
                double median = active.Length == 0 ? 0 : active[active.Length / 2];
                double threshold = Math.Max(1.0, median * 5);
                var spikes = ticks.Select((ms, index) => (ms, tick: firstTick + index)).Where(t => t.ms > threshold).OrderByDescending(t => t.ms).ToList();
                double max = active.Length == 0 ? 0 : active[^1];
                double p99 = active.Length == 0 ? 0 : active[Math.Min(active.Length - 1, (int)(active.Length * 0.99))];
                double perTick = group.TotalMs / Math.Max(1, tickSpan - 2);
                double perCall = group.Calls == 0 ? 0 : group.TotalMs * 1000 / group.Calls;
                int pawns = group.Pawns.Count;
                report.Info($"DoTick {group.Name}: {pawns} pawns, {group.Calls} calls, {perTick:0.0000} ms/tick, {perCall:0.0} us/call, "
                    + $"median {median:0.0000} ms, p99 {p99:0.000} ms, max {max:0.000} ms, {spikes.Count} spike ticks");
                groups.Add(new JsonObject
                {
                    ["group"] = group.Name,
                    ["pawns"] = pawns,
                    ["calls"] = group.Calls,
                    ["totalMs"] = Math.Round(group.TotalMs, 3),
                    ["msPerTick"] = Math.Round(perTick, 5),
                    ["usPerCall"] = Math.Round(perCall, 2),
                    ["medianMsPerTick"] = Math.Round(median, 5),
                    ["p99MsPerTick"] = Math.Round(p99, 4),
                    ["maxMsPerTick"] = Math.Round(max, 4),
                    ["spikes"] = spikes.Count,
                    ["worstTicks"] = new JsonArray([.. spikes.Take(10).Select(s => (JsonNode)new JsonObject { ["tick"] = s.tick, ["ms"] = Math.Round(s.ms, 3) })]),
                });
            }

            return groups;
        }

        public static IEnumerable<(string Name, int Calls, int Pawns)> ScopeGroups()
            => ByName.Values.Select(g => (g.Name, g.Calls, g.Pawns.Count));

        private sealed class Group(string name, int ticks)
        {
            public string Name { get; } = name;

            public ProfilerLabelId Label { get; } = FrameProfiler.GetOrCreateLabelId(ProbeLabelPrefix + name);

            public HashSet<int> Pawns { get; } = [];

            public int Calls { get; set; }

            public double TotalMs { get; set; }

            public double[] PerTick { get; } = new double[Math.Max(0, ticks)];
        }
    }

    /// <summary>读取内置分析器在测量窗口内发布的帧快照，按调用路径累计，并归到 DoTick 探针的各组之下。</summary>
    private static class ProfileReader
    {
        private struct Totals
        {
            public double TotalUs;
            public double SelfUs;
            public long Calls;
            public long AllocBytes;
            public long SelfAllocBytes;
        }

        public static void Read(ValidationReport report, JsonObject profile, int startTick, int endTick, int startOrdinal, int hungry)
        {
            var paths = new Dictionary<int, Totals>();
            var frameTotalsByGroupPath = new Dictionary<int, List<(double Us, FrameProfiler.FrameSnapshot Frame, int Ordinal)>>();
            long droppedEvents = 0;
            int overflowedScopes = 0;
            int frames = 0;
            int torn = 0;
            double frameMs = 0;
            long gen0 = 0, gen1 = 0, gen2 = 0;
            float pauseMs = 0;
            var probeRoots = new Dictionary<int, string>();
            // 读取前已暂停分析器：后台线程不再发布新帧，环形历史中的快照不会被回收复用。
            using (FrameProfiler.BeginRead())
            {
                ProfilerPathTable.Entry[] table = ProfilerPathTable.Entries;
                int tableCount = ProfilerPathTable.Count;
                List<FrameProfiler.FrameSnapshot> window = WindowFrames(startTick, endTick, startOrdinal, out string selection);
                foreach (FrameProfiler.FrameSnapshot snapshot in window)
                {
                    int ordinal = Volatile.Read(ref snapshot.captureOrdinal);
                    var frameGroups = new Dictionary<int, double>();
                    for (int s = 0; s < snapshot.sectionCount; s++)
                    {
                        FrameProfiler.ThreadSection section = snapshot.sections[s];
                        int end = section.firstPathEntry + section.pathEntryCount;
                        for (int i = section.firstPathEntry; i < end; i++)
                        {
                            FrameProfiler.PathEntry entry = snapshot.pathEntries[i];
                            ref Totals totals = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(paths, entry.pathId, out _);
                            totals.TotalUs += entry.totalMicroseconds;
                            totals.SelfUs += entry.selfMicroseconds;
                            totals.Calls += entry.calls;
                            totals.AllocBytes += entry.allocBytes;
                            totals.SelfAllocBytes += entry.selfAllocBytes;
                            if ((uint)entry.pathId < (uint)tableCount && IsProbe(table[entry.pathId].labelId, out string? group))
                            {
                                probeRoots[entry.pathId] = group;
                                frameGroups[entry.pathId] = frameGroups.GetValueOrDefault(entry.pathId) + entry.totalMicroseconds;
                            }
                        }
                    }

                    if (Volatile.Read(ref snapshot.captureOrdinal) != ordinal)
                    {
                        torn++;
                        continue;
                    }

                    frames++;
                    frameMs += snapshot.totalMs;
                    gen0 += snapshot.gen0Collections;
                    gen1 += snapshot.gen1Collections;
                    gen2 += snapshot.gen2Collections;
                    pauseMs += snapshot.pauseMs;
                    droppedEvents += snapshot.droppedEvents;
                    overflowedScopes += snapshot.overflowedScopes;
                    foreach ((int pathId, double us) in frameGroups)
                    {
                        if (!frameTotalsByGroupPath.TryGetValue(pathId, out var perFrame))
                        {
                            perFrame = [];
                            frameTotalsByGroupPath[pathId] = perFrame;
                        }

                        perFrame.Add((us, snapshot, ordinal));
                    }
                }

                profile["window"] = new JsonObject
                {
                    ["startTick"] = startTick,
                    ["endTick"] = endTick,
                    ["selection"] = selection,
                    ["frames"] = frames,
                    ["tornFrames"] = torn,
                    ["frameMs"] = Math.Round(frameMs, 1),
                    ["gcPauseMs"] = Math.Round(pauseMs, 1),
                    ["gen0"] = gen0,
                    ["gen1"] = gen1,
                    ["gen2"] = gen2,
                    ["hungryColonists"] = hungry,
                    ["probeCalls"] = DoTickProbe.ScopeCalls,
                    ["historyCount"] = FrameProfiler.HistoryCount,
                    ["completedFrames"] = FrameProfiler.CompletedFrameCount,
                    ["captureGaps"] = FrameProfiler.CaptureGapCount,
                    ["droppedEvents"] = droppedEvents,
                    ["overflowedScopes"] = overflowedScopes,
                };
                report.Info($"profiler window: ticks {startTick}..{endTick}, {frames} frames ({torn} torn, selected by {selection}), {frameMs:0} ms frame time, "
                    + $"GC pauses {pauseMs:0.0} ms (gen0 {gen0}, gen1 {gen1}, gen2 {gen2}), probe saw {DoTickProbe.ScopeCalls} pawn ticks, "
                    + $"{droppedEvents} dropped events, {overflowedScopes} overflowed scopes, history {FrameProfiler.HistoryCount} frames");

                var groupNodes = new JsonArray();
                foreach (IGrouping<string, int> rootsOfGroup in probeRoots.GroupBy(r => r.Value, r => r.Key).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    groupNodes.Add(DescribeGroup(report, rootsOfGroup.Key, [.. rootsOfGroup], paths, table, tableCount, frameTotalsByGroupPath));
                }

                profile["groups"] = groupNodes;
                profile["topSelfOverall"] = TopByLabel(paths, table, tableCount, root: -1, TopPaths);
            }
        }

        /// <summary>
        /// 测量窗口内的帧：优先按帧记录的 tick 范围（宿主为分析器提供当前 tick）选取；若没有帧带 tick 信息，退回按开始前最新帧的序号选取。
        /// </summary>
        private static List<FrameProfiler.FrameSnapshot> WindowFrames(int startTick, int endTick, int startOrdinal, out string selection)
        {
            var byTicks = new List<FrameProfiler.FrameSnapshot>();
            var byOrdinal = new List<FrameProfiler.FrameSnapshot>();
            int history = FrameProfiler.HistoryCount;
            for (int offset = 0; offset < history; offset++)
            {
                if (!FrameProfiler.TryGetHistorySnapshot(offset, out FrameProfiler.FrameSnapshot? snapshot))
                {
                    continue;
                }

                if (snapshot.tickEnd > startTick && snapshot.tickStart < endTick)
                {
                    byTicks.Add(snapshot);
                }

                if (Volatile.Read(ref snapshot.captureOrdinal) > startOrdinal)
                {
                    byOrdinal.Add(snapshot);
                }
            }

            selection = byTicks.Count > 0 ? "ticks" : "ordinal";
            return byTicks.Count > 0 ? byTicks : byOrdinal;
        }

        /// <summary>当前历史中最新一帧的序号；没有帧时为 -1。</summary>
        public static int NewestOrdinal()
            => FrameProfiler.TryGetHistorySnapshot(0, out FrameProfiler.FrameSnapshot? snapshot) ? Volatile.Read(ref snapshot.captureOrdinal) : -1;

        private static JsonObject DescribeGroup(
            ValidationReport report,
            string group,
            List<int> roots,
            Dictionary<int, Totals> paths,
            ProfilerPathTable.Entry[] table,
            int tableCount,
            Dictionary<int, List<(double Us, FrameProfiler.FrameSnapshot Frame, int Ordinal)>> worstFrames)
        {
            double totalUs = 0;
            long calls = 0;
            long alloc = 0;
            foreach (int root in roots)
            {
                Totals totals = paths[root];
                totalUs += totals.TotalUs;
                calls += totals.Calls;
                alloc += totals.AllocBytes;
            }

            var node = new JsonObject
            {
                ["group"] = group,
                ["calls"] = calls,
                ["totalMs"] = Math.Round(totalUs / 1000, 3),
                ["usPerCall"] = calls == 0 ? 0 : Math.Round(totalUs / calls, 2),
                ["allocBytes"] = alloc,
                ["bytesPerCall"] = calls == 0 ? 0 : Math.Round((double)alloc / calls, 1),
            };
            report.Info($"profiled DoTick {group}: {calls} calls, {totalUs / 1000:0.0} ms, {(calls == 0 ? 0 : totalUs / calls):0.0} us/call, "
                + $"{alloc / 1024.0:0.0} KiB allocated");

            var underRoots = new JsonArray();
            foreach (int root in roots)
            {
                underRoots.Add(new JsonObject
                {
                    ["context"] = PathText(root, table, tableCount, stopAt: -1, maxDepth: 6),
                    ["topSelfByLabel"] = TopByLabel(paths, table, tableCount, root, TopPaths),
                    ["topTotalPaths"] = TopPathsUnder(paths, table, tableCount, root, TopPaths, self: false),
                    ["topSelfPaths"] = TopPathsUnder(paths, table, tableCount, root, TopPaths, self: true),
                });
            }

            node["roots"] = underRoots;
            var worst = new JsonArray();
            foreach ((int root, (double Us, FrameProfiler.FrameSnapshot Frame, int Ordinal) frame) in roots
                .SelectMany(root => worstFrames.GetValueOrDefault(root, []).Select(frame => (root, frame)))
                .OrderByDescending(entry => entry.frame.Us)
                .Take(WorstFramesPerGroup))
            {
                var frameEntries = new Dictionary<int, Totals>();
                FrameProfiler.FrameSnapshot snapshot = frame.Frame;
                for (int s = 0; s < snapshot.sectionCount; s++)
                {
                    FrameProfiler.ThreadSection section = snapshot.sections[s];
                    int end = section.firstPathEntry + section.pathEntryCount;
                    for (int i = section.firstPathEntry; i < end; i++)
                    {
                        FrameProfiler.PathEntry entry = snapshot.pathEntries[i];
                        ref Totals totals = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(frameEntries, entry.pathId, out _);
                        totals.TotalUs += entry.totalMicroseconds;
                        totals.SelfUs += entry.selfMicroseconds;
                        totals.Calls += entry.calls;
                        totals.AllocBytes += entry.allocBytes;
                        totals.SelfAllocBytes += entry.selfAllocBytes;
                    }
                }

                bool consistent = Volatile.Read(ref snapshot.captureOrdinal) == frame.Ordinal;
                worst.Add(new JsonObject
                {
                    ["ordinal"] = frame.Ordinal,
                    ["consistent"] = consistent,
                    ["ticks"] = $"{snapshot.tickStart}..{snapshot.tickEnd}",
                    ["frameMs"] = Math.Round(snapshot.totalMs, 2),
                    ["groupMs"] = Math.Round(frame.Us / 1000, 3),
                    ["gcPauseMs"] = Math.Round(snapshot.pauseMs, 2),
                    ["gc"] = $"{snapshot.gen0Collections}/{snapshot.gen1Collections}/{snapshot.gen2Collections}",
                    ["topSelfPaths"] = TopPathsUnder(frameEntries, table, tableCount, root, 15, self: true),
                });
                report.Info($"worst frame for {group}: ordinal {frame.Ordinal}, ticks {snapshot.tickStart}..{snapshot.tickEnd}, frame {snapshot.totalMs:0.0} ms, "
                    + $"{group} {frame.Us / 1000:0.000} ms, GC pause {snapshot.pauseMs:0.0} ms");
            }

            node["worstFrames"] = worst;
            return node;
        }

        /// <summary>按标签合并 <paramref name="root"/> 之下（root 为 -1 时为全部）路径的自身耗时：同一方法在不同调用路径上的耗时相加。</summary>
        private static JsonArray TopByLabel(Dictionary<int, Totals> paths, ProfilerPathTable.Entry[] table, int tableCount, int root, int count)
        {
            var byLabel = new Dictionary<ushort, Totals>();
            foreach ((int pathId, Totals totals) in paths)
            {
                if ((uint)pathId >= (uint)tableCount || (root >= 0 && (pathId == root || !IsUnder(pathId, root, table, tableCount))))
                {
                    continue;
                }

                ushort label = table[pathId].labelId.Value;
                ref Totals merged = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(byLabel, label, out _);
                merged.SelfUs += totals.SelfUs;
                merged.Calls += totals.Calls;
                merged.SelfAllocBytes += totals.SelfAllocBytes;
            }

            return [.. byLabel.OrderByDescending(p => p.Value.SelfUs).Take(count).Select(p => (JsonNode)new JsonObject
            {
                ["label"] = FrameProfiler.GetLabel(new ProfilerLabelId(p.Key)),
                ["selfMs"] = Math.Round(p.Value.SelfUs / 1000, 3),
                ["calls"] = p.Value.Calls,
                ["selfAllocBytes"] = p.Value.SelfAllocBytes,
            })];
        }

        private static JsonArray TopPathsUnder(Dictionary<int, Totals> paths, ProfilerPathTable.Entry[] table, int tableCount, int root, int count, bool self)
        {
            IEnumerable<KeyValuePair<int, Totals>> under = paths.Where(p => (uint)p.Key < (uint)tableCount && p.Key != root && IsUnder(p.Key, root, table, tableCount));
            under = self ? under.OrderByDescending(p => p.Value.SelfUs) : under.OrderByDescending(p => p.Value.TotalUs);
            return [.. under.Take(count).Select(p => (JsonNode)new JsonObject
            {
                ["path"] = PathText(p.Key, table, tableCount, stopAt: root, maxDepth: 12),
                ["totalMs"] = Math.Round(p.Value.TotalUs / 1000, 3),
                ["selfMs"] = Math.Round(p.Value.SelfUs / 1000, 3),
                ["calls"] = p.Value.Calls,
                ["allocBytes"] = p.Value.AllocBytes,
            })];
        }

        private static bool IsUnder(int pathId, int root, ProfilerPathTable.Entry[] table, int tableCount)
        {
            for (int id = pathId, guard = 0; (uint)id < (uint)tableCount && guard < 512; id = table[id].parentPathId, guard++)
            {
                if (id == root)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>从 <paramref name="pathId"/> 向上到 <paramref name="stopAt"/>（不含）的标签，自上而下用 " > " 连接；超过 <paramref name="maxDepth"/> 层时省略中间。</summary>
        private static string PathText(int pathId, ProfilerPathTable.Entry[] table, int tableCount, int stopAt, int maxDepth)
        {
            var labels = new List<string>();
            for (int id = pathId, guard = 0; (uint)id < (uint)tableCount && id != stopAt && id != ProfilerPathTable.VirtualRoot && guard < 512; id = table[id].parentPathId, guard++)
            {
                labels.Add(FrameProfiler.GetLabel(table[id].labelId));
            }

            labels.Reverse();
            if (labels.Count > maxDepth)
            {
                labels = [.. labels.Take(2), "…", .. labels.Skip(labels.Count - (maxDepth - 3))];
            }

            return string.Join(" > ", labels);
        }

        private static bool IsProbe(ProfilerLabelId label, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? group)
        {
            string text = FrameProfiler.GetLabel(label);
            if (text.StartsWith(ProbeLabelPrefix, StringComparison.Ordinal))
            {
                group = text[ProbeLabelPrefix.Length..];
                return true;
            }

            group = null;
            return false;
        }
    }

    /// <summary>运行前分析器与插桩的设置；结束时恢复，测试进程中后续的套件不受影响。</summary>
    private sealed class ProfilerState
    {
        private bool enabled;
        private bool mainThreadOnly;
        private bool allocTracking;
        private int historyCapacity;
        private bool instrumentation;
        private string[] targets = [];

        public static ProfilerState Capture() => new()
        {
            enabled = FrameProfiler.Enabled,
            mainThreadOnly = FrameProfiler.MainThreadOnly,
            allocTracking = FrameProfiler.AllocationTracking,
            historyCapacity = FrameProfiler.HistoryCapacity,
            instrumentation = ProfilerInstrumentation.IsEnabled,
            targets = [.. ProfilerInstrumentation.Targets],
        };

        public void Restore()
        {
            if (ProfilerInstrumentation.IsSupported)
            {
                ProfilerInstrumentation.SetEnabled(instrumentation);
            }

            ProfilerInstrumentation.SetTargets(targets);
            FrameProfiler.SetAllocationTracking(allocTracking);
            FrameProfiler.SetMainThreadOnly(mainThreadOnly);
            FrameProfiler.SetHistoryCapacity(historyCapacity);
            FrameProfiler.SetPaused(false);
            FrameProfiler.SetEnabled(enabled);
        }
    }
}
