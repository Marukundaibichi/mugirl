using Mugirl.Features.Grazing;
using Mugirl.Features.Milk;
using RimWorld.Planet;
using Verse.AI;

namespace Mugirl.Features.Incidents;

// 野生雪牛娘迁徙经过地图：吃草、停留一段时间后从另一侧离开。需要地图上有足够的野生可食植物。
public class IncidentWorker_Migration : IncidentWorker
{
    private const int MinWildPlants = 40;
    private const int ExitCellCandidates = 8;
    private const int SpawnRadius = 10;
    private static readonly IntRange MigrationCount = new IntRange(4, 6);
    private static readonly IntRange StayTicks = new IntRange(15000, 60000);

    protected override bool CanFireNowSub(IncidentParms parms)
    {
        if (!base.CanFireNowSub(parms) || MugirlMod.Settings?.enableMigrationEvent != true)
        {
            return false;
        }

        if (parms.target is not Map map)
        {
            return false;
        }

        MapComponent_Migration? migration = map.GetComponent<MapComponent_Migration>();
        return migration?.HasActiveMigration != true
            && CountWildGrazePlants(map, MinWildPlants) >= MinWildPlants
            && TryFindStartAndEndCells(map, out _, out _);
    }

    protected override bool TryExecuteWorker(IncidentParms parms)
    {
        if (parms.target is not Map map || CountWildGrazePlants(map, MinWildPlants) < MinWildPlants)
        {
            return false;
        }

        MapComponent_Migration? migration = map.GetComponent<MapComponent_Migration>();
        if (migration?.HasActiveMigration == true)
        {
            return false;
        }

        if (!TryFindStartAndEndCells(map, out IntVec3 start, out IntVec3 end))
        {
            return false;
        }

        List<Pawn> pawns = GenerateMigrationPawns(map.Tile);
        if (pawns.Count == 0)
        {
            return false;
        }

        Rot4 rot = Rot4.FromAngleFlat((map.Center - start).AngleFlat);
        for (int i = 0; i < pawns.Count; i++)
        {
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(start, map, SpawnRadius);
            GenSpawn.Spawn(pawns[i], cell, map, rot);
        }

        migration?.RegisterMigration(pawns, end, StayTicks.RandomInRange);

        SendStandardLetter(
            "Mugirl.Migration.LetterLabel".Translate(),
            "Mugirl.Migration.LetterText".Translate(),
            LetterDefOf.PositiveEvent,
            parms,
            pawns[0]);

        return true;
    }

    private static List<Pawn> GenerateMigrationPawns(PlanetTile tile)
    {
        int count = MigrationCount.RandomInRange;
        List<Pawn> pawns = new List<Pawn>(count);
        for (int i = 0; i < count; i++)
        {
            PawnGenerationRequest request = EventPawnRequestUtility.CreateFactionlessAdultRequest(
                IncidentsDefOf.Mugirl_WildMugirl, tile, unarmed: true);

            Pawn? pawn = PawnGenerator.GeneratePawn(request);
            if (pawn == null)
            {
                continue;
            }

            EventPawnTags.MarkMigrationPawn(pawn);
            EventPawnTags.PreparePassiveWildPawn(pawn);

            pawn.TryGetComp<CompMilkProduction>()?.DevFillToFull(triggerNotify: false);

            pawns.Add(pawn);
        }

        return pawns;
    }

    // 只数到 stopAt 为止；植物组列表与 1.6 遍历的 AllThings 是同一批物品，计数结果相同。
    private static int CountWildGrazePlants(Map map, int stopAt)
    {
        int count = 0;
        List<Thing> plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
        for (int i = 0; i < plants.Count; i++)
        {
            if (plants[i] is not Plant plant || plant.sown || !GrazeUtility.IsMapPlantFood(plant))
            {
                continue;
            }

            count++;
            if (count >= stopAt)
            {
                break;
            }
        }

        return count;
    }

    // 入场格随机；离场格在若干条可达的边缘格中取离入场格最远的一个。
    private static bool TryFindStartAndEndCells(Map map, out IntVec3 start, out IntVec3 end)
    {
        end = IntVec3.Invalid;
        if (!RCellFinder.TryFindRandomPawnEntryCell(out start, map, CellFinder.EdgeRoadChance_Animal))
        {
            return false;
        }

        IntVec3 startLocal = start;
        TraverseParms traverseParms = TraverseParms.For(TraverseMode.NoPassClosedDoors).WithFenceblocked(forceFenceblocked: true);
        for (int i = 0; i < ExitCellCandidates; i++)
        {
            if (!CellFinder.TryFindRandomEdgeCellWith(
                c => map.reachability.CanReach(startLocal, c, PathEndMode.OnCell, traverseParms),
                map,
                CellFinder.EdgeRoadChance_Ignore,
                out IntVec3 candidate))
            {
                break;
            }

            if (!end.IsValid || candidate.DistanceToSquared(start) > end.DistanceToSquared(start))
            {
                end = candidate;
            }
        }

        return end.IsValid;
    }
}
