using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Mugirl
{
    public class IncidentWorker_Mugirl_Migration : IncidentWorker
    {
        private const int MinWildPlants = 40;
        private static readonly IntRange MigrationCount = new IntRange(4, 6);
        private static readonly IntRange StayTicks = new IntRange(15000, 60000);

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms) || MugirlMod.Settings?.enableMugirlMigrationEvent != true)
            {
                return false;
            }

            Map map = parms.target as Map;
            if (map == null)
            {
                return false;
            }

            return CountWildGrazePlants(map, MinWildPlants) >= MinWildPlants
                && TryFindStartAndEndCells(map, out _, out _);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null || CountWildGrazePlants(map, MinWildPlants) < MinWildPlants)
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
                Pawn pawn = pawns[i];
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(start, map, 10);
                GenSpawn.Spawn(pawn, cell, map, rot);
            }

            MapComponent_MugirlMigration migration = map.GetComponent<MapComponent_MugirlMigration>();
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
                PawnGenerationRequest request = new PawnGenerationRequest(
                    Mugirl_DefOf.Mugirl_EscapeWildSlave,
                    null,
                    PawnGenerationContext.NonPlayer,
                    tile,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: true,
                    forceAddFreeWarmLayerIfNeeded: false,
                    allowGay: true,
                    allowPregnant: false,
                    forceRecruitable: true,
                    fixedGender: Gender.Female,
                    developmentalStages: DevelopmentalStage.Adult);

                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (pawn == null)
                {
                    continue;
                }

                MugirlEventUtility.WearBikiniOnly(pawn);
                MugirlEventUtility.MarkMigrationPawn(pawn);
                MugirlEventUtility.PreparePassiveWildPawn(pawn);

                CompMooMilkable milkComp = pawn.TryGetComp<CompMooMilkable>();
                milkComp?.DevFillToFull(triggerNotify: false);

                pawns.Add(pawn);
            }

            return pawns;
        }
        private static int CountWildGrazePlants(Map map, int stopAt)
        {
            if (map?.listerThings?.AllThings == null)
            {
                return 0;
            }

            int count = 0;
            List<Thing> things = map.listerThings.AllThings;
            for (int i = 0; i < things.Count; i++)
            {
                Plant plant = things[i] as Plant;
                if (plant == null || plant.sown || !MugirlGrazeUtility.IsMapPlantFood(plant))
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

        private static bool TryFindStartAndEndCells(Map map, out IntVec3 start, out IntVec3 end)
        {
            if (!RCellFinder.TryFindRandomPawnEntryCell(out start, map, CellFinder.EdgeRoadChance_Animal))
            {
                end = IntVec3.Invalid;
                return false;
            }

            end = IntVec3.Invalid;
            for (int i = 0; i < 8; i++)
            {
                IntVec3 startLocal = start;
                if (!CellFinder.TryFindRandomEdgeCellWith(
                    c => map.reachability.CanReach(startLocal, c, PathEndMode.OnCell, TraverseParms.For(TraverseMode.NoPassClosedDoors).WithFenceblocked(forceFenceblocked: true)),
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

    public class MapComponent_MugirlMigration : MapComponent
    {
        private const int ForcedGrazeIntervalTicks = 300;
        private const float ForcedGrazeSearchRadius = 30f;

        private List<Pawn> migrationPawns = new List<Pawn>();
        private List<int> nextGrazeTicks = new List<int>();
        private IntVec3 exitCell = IntVec3.Invalid;
        private int ticksUntilDeparture;

        public MapComponent_MugirlMigration(Map map) : base(map)
        {
        }

        public void RegisterMigration(List<Pawn> pawns, IntVec3 exit, int stayTicks)
        {
            migrationPawns.RemoveAll(p => p == null || p.Destroyed);
            EnsureGrazeTicksAligned();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null && !migrationPawns.Contains(pawn))
                {
                    migrationPawns.Add(pawn);
                    nextGrazeTicks.Add(Find.TickManager.TicksGame + Rand.RangeInclusive(30, ForcedGrazeIntervalTicks));
                }
            }

            exitCell = exit;
            ticksUntilDeparture = stayTicks;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (migrationPawns.Count == 0)
            {
                ticksUntilDeparture = 0;
                return;
            }

            for (int i = migrationPawns.Count - 1; i >= 0; i--)
            {
                Pawn pawn = migrationPawns[i];
                if (pawn == null || pawn.Destroyed)
                {
                    RemoveMigrationPawnAt(i);
                    continue;
                }

                if (MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction))
                {
                    MugirlEventUtility.ClearTemporaryEventTags(pawn);
                    RemoveMigrationPawnAt(i);
                    continue;
                }

                MugirlEventUtility.EnsureBikiniOnly(pawn);
                TryForcedGraze(pawn, i);
            }

            if (migrationPawns.Count == 0)
            {
                ticksUntilDeparture = 0;
                return;
            }

            ticksUntilDeparture--;
            if (ticksUntilDeparture > 0)
            {
                return;
            }

            BeginDeparture();
        }

        private void TryForcedGraze(Pawn pawn, int index)
        {
            if (pawn == null || pawn.Dead || pawn.Downed || !pawn.Spawned || pawn.Map != map)
            {
                return;
            }

            EnsureGrazeTicksAligned();
            int ticksGame = Find.TickManager.TicksGame;
            if (index < 0 || index >= nextGrazeTicks.Count || ticksGame < nextGrazeTicks[index])
            {
                return;
            }

            nextGrazeTicks[index] = ticksGame + ForcedGrazeIntervalTicks;
            Thing plant = FindGrazePlant(pawn);
            if (plant == null)
            {
                return;
            }

            TryStartGrazeJob(pawn, plant);
        }

        private static Thing FindGrazePlant(Pawn pawn)
        {
            TraverseParms traverseParms = TraverseParms.For(pawn, DangerUtility.NormalMaxDanger(pawn));
            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForGroup(ThingRequestGroup.Plant),
                PathEndMode.Touch,
                traverseParms,
                ForcedGrazeSearchRadius,
                thing => IsForcedGrazePlant(pawn, thing));
        }

        private static bool IsForcedGrazePlant(Pawn pawn, Thing thing)
        {
            Plant plant = thing as Plant;
            return plant != null
                && !plant.sown
                && MugirlGrazeUtility.IsMapPlantFood(plant)
                && !thing.IsForbidden(pawn)
                && pawn.CanReserve(thing, 1, 1, null, ignoreOtherReservations: true);
        }

        private static void TryStartGrazeJob(Pawn pawn, Thing plant)
        {
            if (pawn?.jobs == null || plant == null || !plant.Spawned || plant.Destroyed)
            {
                return;
            }

            if (RopingService.HasAnyRope(pawn) || pawn.Drafted || pawn.InMentalState)
            {
                return;
            }

            if (pawn.Map?.physicalInteractionReservationManager?.IsReserved(pawn) == true)
            {
                return;
            }

            Job curJob = pawn.CurJob;
            if (curJob?.def == JobDefOf.Ingest)
            {
                return;
            }

            if (curJob != null && !curJob.def.isIdle && curJob.def != JobDefOf.GotoWander)
            {
                return;
            }

            if (!pawn.CanReserve(plant, 1, 1, null, ignoreOtherReservations: false))
            {
                return;
            }

            Job job = JobMaker.MakeJob(JobDefOf.Ingest, plant);
            job.count = 1;
            job.overeat = true;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced, tag: JobTag.Misc);
        }

        private void RemoveMigrationPawnAt(int index)
        {
            migrationPawns.RemoveAt(index);
            if (nextGrazeTicks != null && index >= 0 && index < nextGrazeTicks.Count)
            {
                nextGrazeTicks.RemoveAt(index);
            }
        }

        private void EnsureGrazeTicksAligned()
        {
            if (nextGrazeTicks == null)
            {
                nextGrazeTicks = new List<int>();
            }

            while (nextGrazeTicks.Count < migrationPawns.Count)
            {
                nextGrazeTicks.Add(Find.TickManager.TicksGame + Rand.RangeInclusive(30, ForcedGrazeIntervalTicks));
            }

            while (nextGrazeTicks.Count > migrationPawns.Count)
            {
                nextGrazeTicks.RemoveAt(nextGrazeTicks.Count - 1);
            }
        }

        private void BeginDeparture()
        {
            List<Pawn> departing = new List<Pawn>();
            for (int i = 0; i < migrationPawns.Count; i++)
            {
                Pawn pawn = migrationPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction))
                {
                    continue;
                }

                if (pawn.Map != map)
                {
                    continue;
                }

                departing.Add(pawn);
            }

            if (departing.Count > 0 && exitCell.IsValid)
            {
                LordMaker.MakeNewLord(null, new LordJob_ExitMapNear(exitCell, LocomotionUrgency.Walk), map, departing);
            }

            migrationPawns.Clear();
            nextGrazeTicks.Clear();
            ticksUntilDeparture = 0;
            exitCell = IntVec3.Invalid;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref migrationPawns, "migrationPawns", LookMode.Reference);
            Scribe_Collections.Look(ref nextGrazeTicks, "nextGrazeTicks", LookMode.Value);
            Scribe_Values.Look(ref exitCell, "exitCell", IntVec3.Invalid);
            Scribe_Values.Look(ref ticksUntilDeparture, "ticksUntilDeparture", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (migrationPawns == null)
                {
                    migrationPawns = new List<Pawn>();
                }

                EnsureGrazeTicksAligned();
            }
        }
    }
}
