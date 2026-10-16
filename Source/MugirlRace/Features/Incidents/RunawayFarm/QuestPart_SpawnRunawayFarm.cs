using RimWorld.Planet;
using Verse.AI.Group;

namespace Mugirl.Features.Incidents;

// 逃跑牧场（QuestPart，按类名存档）：站点地图生成后搭建牧场、生成牧场主与逃跑的雪牛娘。字段与 Scribe 标签是存档契约。
// 牧场布局在 QuestPart_SpawnRunawayFarm.Layout.cs。
public partial class QuestPart_SpawnRunawayFarm : QuestPart
{
    private const int RunawayCount = 5;
    private const int RancherStayTicks = 12 * GenDate.TicksPerDay;
    private const int RancherCellRadius = 4;
    private const int RunawayCellRadius = 7;
    private const int RunawayWanderRadius = 9;

    public string? inSignal;
    public Site? site;
    public string? protectedTargetTag;
    public List<Pawn> runawayPawns = new List<Pawn>();
    public List<Thing> hitches = new List<Thing>();
    public List<Thing> farmThings = new List<Thing>();
    public Pawn? rancher;
    public Faction? rancherFaction;
    public bool spawned;

    public override IEnumerable<GlobalTargetInfo> QuestLookTargets
    {
        get
        {
            foreach (GlobalTargetInfo target in base.QuestLookTargets)
            {
                yield return target;
            }

            if (site != null)
            {
                yield return site;
            }

            if (runawayPawns != null)
            {
                for (int i = 0; i < runawayPawns.Count; i++)
                {
                    if (runawayPawns[i] != null)
                    {
                        yield return runawayPawns[i];
                    }
                }
            }
        }
    }

    public override bool IncreasesPopulation => true;

    public override void Notify_QuestSignalReceived(Signal signal)
    {
        base.Notify_QuestSignalReceived(signal);
        if (signal.tag == inSignal && !spawned)
        {
            SpawnFarm(signal);
        }
    }

    private void SpawnFarm(Signal signal)
    {
        Map? map = ResolveMap(signal);
        if (map == null)
        {
            MugirlLog.WarningOnce("RunawayFarm.Spawn.NoMap", "Mugirl.RunawayFarm.Log.NoMap".Translate().ToString());
            return;
        }

        IntVec3 center = FindFarmCenter(map);
        // 没有可用的非敌对派系时牧场与牧场主都无派系（与 1.6 相同）。
        GameUtility.TryGetRandomNonHostileFaction(out rancherFaction, allowHidden: false, minTechLevel: TechLevel.Medieval);
        BuildFarm(map, center);
        SpawnRancher(map, center);
        SpawnRunaways(map, center);
        spawned = true;

        GameUtility.TryReceiveLetter(
            "Mugirl.RunawayFarm.ArrivedLetterLabel".Translate(),
            "Mugirl.RunawayFarm.ArrivedLetterText".Translate(),
            LetterDefOf.NeutralEvent,
            runawayPawns.Count > 0 ? new LookTargets(runawayPawns) : new LookTargets(site));
    }

    private Map? ResolveMap(Signal signal)
    {
        if (site?.HasMap == true)
        {
            return site.Map;
        }

        if (SignalArgsUtility.TryGetLookTargets(signal.args, "SUBJECT", out LookTargets targets))
        {
            GlobalTargetInfo target = targets.PrimaryTarget;
            if (target.WorldObject is MapParent parent && parent.HasMap)
            {
                return parent.Map;
            }
        }

        return null;
    }

    private void SpawnRancher(Map map, IntVec3 center)
    {
        rancher = PawnGenerator.GeneratePawn(EventPawnRequestUtility.CreateVisitorRequest(rancherFaction, map.Tile));
        if (rancher == null)
        {
            return;
        }

        AddProtectedTag(rancher);
        IntVec3 cell = center + new IntVec3(-4, 0, 0);
        if (!CellFinder.TryFindRandomCellNear(cell, map, RancherCellRadius, c => c.Standable(map) && !c.Fogged(map), out cell))
        {
            cell = center;
        }

        GenSpawn.Spawn(rancher, cell, map);
        LordMaker.MakeNewLord(
            rancher.Faction,
            new LordJob_WaitForDurationThenExit(cell, RancherStayTicks),
            map,
            Gen.YieldSingle(rancher));
    }

    private void SpawnRunaways(Map map, IntVec3 center)
    {
        for (int i = 0; i < RunawayCount; i++)
        {
            if (!CellFinder.TryFindRandomCellNear(center, map, RunawayCellRadius, c => c.Standable(map) && !c.Fogged(map), out IntVec3 cell))
            {
                cell = center;
            }

            Pawn? pawn = GenerateRunaway(map.Tile);
            if (pawn == null)
            {
                continue;
            }

            GenSpawn.Spawn(pawn, cell, map);
            AddProtectedTag(pawn);
            RunawayWanderUtility.TryForceWander(pawn, center, RunawayWanderRadius);
            runawayPawns.Add(pawn);
        }
    }

    private static Pawn? GenerateRunaway(PlanetTile tile)
    {
        PawnGenerationRequest request = EventPawnRequestUtility.CreateFactionlessAdultRequest(
            IncidentsDefOf.Mugirl_WildMugirl, tile, unarmed: true);

        Pawn? pawn = PawnGenerator.GeneratePawn(request);
        if (pawn == null)
        {
            return null;
        }

        EventPawnTags.MarkRunawayFarmPawn(pawn);
        EventPawnTags.PreparePassiveWildPawn(pawn);

        HediffDef panicDef = IncidentsDefOf.Mugirl_RunawayPanic;
        if (pawn.health?.hediffSet != null && !pawn.health.hediffSet.HasHediff(panicDef))
        {
            pawn.health.AddHediff(panicDef);
        }

        return pawn;
    }

    private void TrackFarmThing(Thing? thing)
    {
        if (thing == null)
        {
            return;
        }

        AddProtectedTag(thing);
        farmThings.Add(thing);
    }

    // 牧场主、雪牛娘与牧场建筑带受保护标签：玩家伤害它们时任务失败。
    private void AddProtectedTag(Thing thing)
    {
        if (string.IsNullOrEmpty(protectedTargetTag))
        {
            return;
        }

        thing.questTags ??= new List<string>();
        if (!thing.questTags.Contains(protectedTargetTag))
        {
            thing.questTags.Add(protectedTargetTag);
        }
    }

    internal void ClearProtectedTag(Thing? thing)
    {
        if (thing == null || string.IsNullOrEmpty(protectedTargetTag))
        {
            return;
        }

        thing.questTags?.Remove(protectedTargetTag);
    }

    public override void Cleanup()
    {
        base.Cleanup();
        if (runawayPawns == null)
        {
            return;
        }

        for (int i = 0; i < runawayPawns.Count; i++)
        {
            Pawn pawn = runawayPawns[i];
            if (pawn != null && !pawn.Destroyed)
            {
                EventPawnTags.ClearRunawayPanic(pawn);
                EventPawnTags.ClearTemporaryEventTags(pawn);
                ClearProtectedTag(pawn);
            }
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref inSignal, "inSignal");
        Scribe_References.Look(ref site, "site");
        Scribe_Values.Look(ref protectedTargetTag, "protectedTargetTag");
        Scribe_Collections.Look(ref runawayPawns, "runawayPawns", LookMode.Reference);
        Scribe_Collections.Look(ref hitches, "hitches", LookMode.Reference);
        Scribe_Collections.Look(ref farmThings, "farmThings", LookMode.Reference);
        Scribe_References.Look(ref rancher, "rancher");
        Scribe_References.Look(ref rancherFaction, "rancherFaction");
        Scribe_Values.Look(ref spawned, "spawned", false);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            runawayPawns ??= new List<Pawn>();
            hitches ??= new List<Thing>();
            farmThings ??= new List<Thing>();
        }
    }
}
