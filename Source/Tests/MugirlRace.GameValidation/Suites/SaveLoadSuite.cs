using System.IO;
using System.Reflection;
using Mugirl.GameValidation.Core;

namespace Mugirl.GameValidation.Suites;

/// <summary>真实存读档：用 GameDataSaveLoader 保存带雪牛娘的游戏，在同一进程中读回，核对 Pawn 与 Mod 组件，再运行一段时间。</summary>
internal static class SaveLoadSuite
{
    public const string Name = "saveload";

    private const int TicksAfterLoad = 300;

    public static ValidationSuite Create()
    {
        // 存档前的快照在两个步骤之间共享；每次 Create() 一份。
        var state = new RoundTrip();
        return new ValidationSuite(
            Name,
            "save a game with Mugirl colonists through GameDataSaveLoader, load it in the same process, compare pawns and the mod's game and map components, then run ticks",
            SuiteNeeds.MugirlMod,
            [
                new ValidationStep("save", ctx => Save(ctx, state), TimeoutSeconds: 180),
                new ValidationStep("load", ctx => Load(ctx, state), TimeoutSeconds: 420),
            ]);
    }

    private static IEnumerable<Wait> Save(ValidationContext ctx, RoundTrip state)
    {
        ValidationReport report = ctx.Report;
        Assembly modAssembly = ctx.RequireTarget().Assembly ?? throw new InvalidOperationException("MugirlRace.dll is not loaded");
        state.Pawns.Clear();
        foreach (Pawn pawn in GameFixtures.EnsureMugirlColonists(ctx, SpawnSuite.ColonistCount))
        {
            state.Pawns.Add(PawnSnapshot.Of(pawn));
        }

        state.GameComponents = ComponentNames(Current.Game.components, modAssembly);
        state.MapComponents = ComponentNames(ctx.Map.components, modAssembly);
        state.Tick = Find.TickManager.TicksGame;
        report.Info($"mod game components: {string.Join(", ", state.GameComponents)}; map components: {string.Join(", ", state.MapComponents)}");

        var save = new SaveWait(ValidationContext.SaveName(Name));
        yield return save;
        state.SaveName = save.SaveName;
        if (!report.Check(File.Exists(save.FilePath) && new FileInfo(save.FilePath).Length > 0, "the game is saved", () => "no save at " + save.FilePath))
        {
            state.SaveName = null;
            yield break;
        }

        string text = File.ReadAllText(save.FilePath);
        report.Info($"saved {save.FilePath}: {text.Length / 1024} KiB, {CountOccurrences(text, "Class=\"Mugirl.")} Class=\"Mugirl.* nodes");
        report.Check(!text.Contains("Mugirl.GameValidation.", StringComparison.Ordinal), "the save holds no test-mod types",
            () => "a class of the test assembly was written into the save");
    }

    private static IEnumerable<Wait> Load(ValidationContext ctx, RoundTrip state)
    {
        ValidationReport report = ctx.Report;
        if (state.SaveName is not { } saveName)
        {
            report.Skip("load", "the save step did not write a save");
            yield break;
        }

        yield return new LoadWait(saveName);
        Assembly modAssembly = ctx.RequireTarget().Assembly ?? throw new InvalidOperationException("MugirlRace.dll is not loaded");
        report.Check(Find.TickManager.TicksGame == state.Tick, "the loaded game resumes at the saved tick",
            () => $"saved {state.Tick}, loaded {Find.TickManager.TicksGame}");
        foreach (PawnSnapshot before in state.Pawns)
        {
            Pawn? pawn = ValidationContext.FindPawn(before.Id);
            if (pawn is null)
            {
                report.Fail($"Mugirl {before.Id} is loaded", "not found in the loaded game");
                continue;
            }

            PawnSnapshot after = PawnSnapshot.Of(pawn);
            report.Check(after == before, $"Mugirl {before.Id} ({before.Name}) round-trips", () => $"before {before}, after {after}");
        }

        List<string> gameComponents = ComponentNames(Current.Game.components, modAssembly);
        report.Check(gameComponents.SequenceEqual(state.GameComponents), "the mod's game components are restored",
            () => $"before [{string.Join(", ", state.GameComponents)}], after [{string.Join(", ", gameComponents)}]");
        List<string> mapComponents = ComponentNames(ctx.Map.components, modAssembly);
        report.Check(mapComponents.SequenceEqual(state.MapComponents), "the mod's map components are restored",
            () => $"before [{string.Join(", ", state.MapComponents)}], after [{string.Join(", ", mapComponents)}]");

        yield return Wait.Ticks(TicksAfterLoad);
        foreach (PawnSnapshot before in state.Pawns)
        {
            Pawn? pawn = ValidationContext.FindPawn(before.Id);
            report.Check(pawn is { Spawned: true, Dead: false }, $"Mugirl {before.Id} lives on after {TicksAfterLoad} ticks",
                () => pawn is null ? "not found" : $"spawned {pawn.Spawned}, dead {pawn.Dead}");
        }
    }

    private static List<string> ComponentNames<T>(IEnumerable<T> components, Assembly modAssembly)
        where T : class
        => [.. components.Select(c => c.GetType()).Where(t => t.Assembly == modAssembly).Select(t => t.FullName ?? t.Name).Order(StringComparer.Ordinal)];

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private sealed class RoundTrip
    {
        public List<PawnSnapshot> Pawns { get; } = [];

        public List<string> GameComponents { get; set; } = [];

        public List<string> MapComponents { get; set; } = [];

        public int Tick { get; set; }

        public string? SaveName { get; set; }
    }

    /// <summary>读档前后应当相同的 Pawn 数据（读档前后都不推进 Tick）。</summary>
    private sealed record PawnSnapshot(
        int Id, string Name, string Def, string Kind, bool PlayerFaction, IntVec3 Position, long AgeTicks,
        int Hediffs, int Apparel, int Equipment, int Inventory, int Comps)
    {
        public static PawnSnapshot Of(Pawn pawn) => new(
            pawn.thingIDNumber,
            pawn.Name?.ToStringFull ?? pawn.LabelShort,
            pawn.def.defName,
            pawn.kindDef?.defName ?? "-",
            pawn.Faction == Faction.OfPlayer,
            pawn.Position,
            pawn.ageTracker.AgeBiologicalTicks,
            pawn.health.hediffSet.hediffs.Count,
            pawn.apparel?.WornApparelCount ?? 0,
            pawn.equipment?.AllEquipmentListForReading.Count ?? 0,
            pawn.inventory?.innerContainer.Count ?? 0,
            pawn.AllComps.Count);
    }
}
