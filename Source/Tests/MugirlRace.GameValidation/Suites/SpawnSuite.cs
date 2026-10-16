using Mugirl.GameValidation.Core;

namespace Mugirl.GameValidation.Suites;

/// <summary>生成雪牛娘殖民者并让游戏运行 2000 Tick：生成、装备、需求、Hediff、渲染树与各 Comp 的 Tick 都在真实游戏中执行一遍。</summary>
internal static class SpawnSuite
{
    public const string Name = "spawn";

    /// <summary>生成的雪牛娘数量。</summary>
    public const int ColonistCount = 3;

    /// <summary>生成后运行的游戏 Tick 数（约 33 游戏秒，覆盖 250 Tick 的稀有 Tick 与 2000 Tick 的长 Tick 各至少一次）。</summary>
    public const int TicksToRun = 2000;

    public static ValidationSuite Create() => new(
        Name,
        $"generate {ColonistCount} Mugirl colonists on the quick-test map and run {TicksToRun} ticks without errors",
        SuiteNeeds.MugirlMod,
        [
            new ValidationStep("generate", Generate),
            new ValidationStep("tick", RunTicks, TimeoutSeconds: 300),
        ]);

    private static IEnumerable<Wait> Generate(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        List<Pawn> pawns = GameFixtures.EnsureMugirlColonists(ctx, ColonistCount);
        foreach (Pawn pawn in pawns)
        {
            string id = pawn.ThingID;
            report.Check(pawn.def.defName == GameFixtures.MugirlRaceDefName, id + " is a Mugirl", () => pawn.def.defName);
            report.Check(pawn.Spawned && !pawn.Dead && pawn.IsColonist, id + " is a spawned, living colonist",
                () => $"spawned {pawn.Spawned}, dead {pawn.Dead}, faction {pawn.Faction}");
            report.Info($"{id}: {pawn.LabelShort}, {pawn.gender}, age {pawn.ageTracker.AgeBiologicalYears}, {pawn.apparel?.WornApparelCount ?? 0} apparel, "
                + $"{pawn.health.hediffSet.hediffs.Count} hediffs, {pawn.AllComps.Count} comps, weapon {pawn.equipment?.Primary?.def.defName ?? "-"}");
        }

        // 让渲染树与界面至少运行几帧（暂停中也绘制）。
        yield return Wait.Frames(5);
    }

    private static IEnumerable<Wait> RunTicks(ValidationContext ctx)
    {
        ValidationReport report = ctx.Report;
        List<int> ids = [.. GameFixtures.EnsureMugirlColonists(ctx, ColonistCount).Select(p => p.thingIDNumber)];
        int start = Find.TickManager.TicksGame;
        yield return Wait.Ticks(TicksToRun, timeoutSeconds: 240);
        int advanced = Find.TickManager.TicksGame - start;
        report.Check(advanced >= TicksToRun, $"the game ran {TicksToRun} ticks", () => $"only {advanced} ticks");
        foreach (int id in ids)
        {
            Pawn? pawn = ValidationContext.FindPawn(id);
            report.Check(pawn is { Spawned: true, Dead: false } && pawn.IsColonist, $"Mugirl {id} is still a spawned, living colonist",
                () => pawn is null ? "not found" : $"spawned {pawn.Spawned}, dead {pawn.Dead}, faction {pawn.Faction}");
        }
    }
}
