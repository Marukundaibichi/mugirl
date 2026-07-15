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

            MapComponent_MugirlMigration migration = map.GetComponent<MapComponent_MugirlMigration>();
            return migration?.HasActiveMigration != true
                && CountWildGrazePlants(map, MinWildPlants) >= MinWildPlants
                && TryFindStartAndEndCells(map, out _, out _);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null || CountWildGrazePlants(map, MinWildPlants) < MinWildPlants)
            {
                return false;
            }

            MapComponent_MugirlMigration migration = map.GetComponent<MapComponent_MugirlMigration>();
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
                Pawn pawn = pawns[i];
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(start, map, 10);
                GenSpawn.Spawn(pawn, cell, map, rot);
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
                PawnGenerationRequest request = new PawnGenerationRequest(
                    Mugirl_DefOf.Mugirl_WildMugirl,
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
                    dontGiveWeapon: true,
                    fixedGender: Gender.Female,
                    developmentalStages: DevelopmentalStage.Adult);
                request.ForceNoIdeoGear = true;

                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (pawn == null)
                {
                    continue;
                }

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
        private const int CurrentDataVersion = 2;
        private const int ForcedGrazeIntervalTicks = 300;
        private const int PunisherKillWindowTicks = 15000;
        private const float ForcedGrazeSearchRadius = 30f;
        private const float FoodSatisfiedTolerance = 0.02f;

        private List<Pawn> migrationPawns = new List<Pawn>();
        private List<int> nextGrazeTicks = new List<int>();
        private List<int> playerKilledMigrationPawnIds = new List<int>();
        private IntVec3 exitCell = IntVec3.Invalid;
        private int ticksUntilDeparture;
        private int killWindowTicksRemaining;
        private int originalMigrationCount;
        private int killedMigrationPawns;
        private bool migrationOutcomeFailed;
        private bool departureStarted;
        private int dataVersion = CurrentDataVersion;

        public bool HasActiveMigration => migrationPawns != null && migrationPawns.Count > 0;

        public MapComponent_MugirlMigration(Map map) : base(map)
        {
        }

        public void RegisterMigration(List<Pawn> pawns, IntVec3 exit, int stayTicks)
        {
            migrationPawns.RemoveAll(p => p == null || p.Destroyed);
            EnsureGrazeTicksAligned();
            int currentTick = MugirlTickUtility.CurrentGameTickOrFallback(0);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null && !migrationPawns.Contains(pawn))
                {
                    migrationPawns.Add(pawn);
                    nextGrazeTicks.Add(currentTick + Rand.RangeInclusive(30, ForcedGrazeIntervalTicks));
                }
            }

            exitCell = exit;
            ticksUntilDeparture = stayTicks;
            originalMigrationCount = migrationPawns.Count;
            killedMigrationPawns = 0;
            playerKilledMigrationPawnIds.Clear();
            killWindowTicksRemaining = PunisherKillWindowTicks;
            migrationOutcomeFailed = originalMigrationCount == 0;
            departureStarted = false;
        }

        internal static void NotifyMigrationPawnAttacked(Pawn pawn)
        {
            Map map = pawn?.MapHeld;
            map?.GetComponent<MapComponent_MugirlMigration>()?.NotifyPawnAttacked(pawn);
        }

        private void NotifyPawnAttacked(Pawn pawn)
        {
            if (pawn == null || !migrationPawns.Contains(pawn))
            {
                return;
            }

            BeginDeparture(LocomotionUrgency.Sprint, preferBestExit: true);
        }

        internal static void NotifyMigrationPawnKilled(Pawn pawn, DamageInfo? dinfo)
        {
            Map map = pawn?.MapHeld;
            MapComponent_MugirlMigration component = map?.GetComponent<MapComponent_MugirlMigration>();
            if (component == null || pawn == null || !component.migrationPawns.Contains(pawn))
            {
                return;
            }

            if (WasDirectlyKilledByPlayer(dinfo)
                && !component.playerKilledMigrationPawnIds.Contains(pawn.thingIDNumber))
            {
                component.playerKilledMigrationPawnIds.Add(pawn.thingIDNumber);
            }
        }

        private static bool WasDirectlyKilledByPlayer(DamageInfo? dinfo)
        {
            if (!dinfo.HasValue)
            {
                return false;
            }

            DamageInfo fatalDamage = dinfo.Value;
            return fatalDamage.Def != null
                && fatalDamage.Category != DamageInfo.SourceCategory.Collapse
                && fatalDamage.Instigator != null
                && MugirlWildSlaveUtility.IsPlayerFaction(fatalDamage.Instigator.Faction);
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
                if (pawn != null && pawn.Dead)
                {
                    bool killedByPlayer = playerKilledMigrationPawnIds.Contains(pawn.thingIDNumber);
                    playerKilledMigrationPawnIds.Remove(pawn.thingIDNumber);
                    if (!migrationOutcomeFailed && killWindowTicksRemaining > 0 && killedByPlayer)
                    {
                        killedMigrationPawns++;
                    }
                    else
                    {
                        migrationOutcomeFailed = true;
                    }

                    RemoveMigrationPawnAt(i);
                    continue;
                }

                if (pawn == null || pawn.Destroyed || !pawn.Spawned || pawn.Map != map)
                {
                    migrationOutcomeFailed = true;
                    RemoveMigrationPawnAt(i);
                    continue;
                }

                if (MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction))
                {
                    migrationOutcomeFailed = true;
                    MugirlEventUtility.ClearTemporaryEventTags(pawn);
                    RemoveMigrationPawnAt(i);
                    continue;
                }

                if (!departureStarted)
                {
                    TryForcedGraze(pawn, i);
                }
            }

            if (migrationPawns.Count == 0)
            {
                ResolveMigrationOutcome();
                ticksUntilDeparture = 0;
                return;
            }

            if (killWindowTicksRemaining > 0)
            {
                killWindowTicksRemaining--;
                if (killWindowTicksRemaining <= 0)
                {
                    migrationOutcomeFailed = true;
                    if (departureStarted)
                    {
                        AbandonMigrationTracking();
                        return;
                    }
                }
            }

            if (departureStarted)
            {
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

            if (!ShouldGrazeForFood(pawn))
            {
                return;
            }

            EnsureGrazeTicksAligned();
            int ticksGame = MugirlTickUtility.CurrentGameTickOrFallback(0);
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

            if (!ShouldGrazeForFood(pawn))
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

        private static bool ShouldGrazeForFood(Pawn pawn)
        {
            Need_Food food = pawn?.needs?.food;
            return food != null && food.CurLevel < food.MaxLevel - FoodSatisfiedTolerance;
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
                int currentTick = MugirlTickUtility.CurrentGameTickOrFallback(0);
                nextGrazeTicks.Add(currentTick + Rand.RangeInclusive(30, ForcedGrazeIntervalTicks));
            }

            while (nextGrazeTicks.Count > migrationPawns.Count)
            {
                nextGrazeTicks.RemoveAt(nextGrazeTicks.Count - 1);
            }
        }

        private void ReleaseLegacyBikiniLocks()
        {
            foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
            {
                if (!MugirlEventUtility.IsMigrationPawn(pawn) || MugirlEventUtility.IsRunawayFarmPawn(pawn))
                {
                    continue;
                }

                UnlockLegacyBikini(pawn);
            }
        }

        private static void UnlockLegacyBikini(Pawn pawn)
        {
            ThingDef bikiniDef = MugirlContentDefOf.Mugirl_Bikini;
            if (pawn?.apparel == null || bikiniDef == null)
            {
                return;
            }

            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = 0; i < wornApparel.Count; i++)
            {
                Apparel apparel = wornApparel[i];
                if (apparel?.def == bikiniDef)
                {
                    pawn.apparel.Unlock(apparel);
                }
            }
        }

        private void BeginDeparture()
        {
            BeginDeparture(LocomotionUrgency.Walk, preferBestExit: false);
        }

        private void BeginDeparture(LocomotionUrgency urgency, bool preferBestExit)
        {
            if (departureStarted)
            {
                if (!preferBestExit)
                {
                    return;
                }

                bool needsBestExitReroute = false;
                for (int i = 0; i < migrationPawns.Count; i++)
                {
                    Pawn pawn = migrationPawns[i];
                    if (pawn != null && pawn.Spawned && !pawn.Dead && pawn.Map == map
                        && !(pawn.GetLord()?.LordJob is LordJob_ExitMapBest))
                    {
                        needsBestExitReroute = true;
                        break;
                    }
                }

                if (!needsBestExitReroute)
                {
                    return;
                }
            }

            departureStarted = true;
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

                pawn.GetLord()?.RemovePawn(pawn);
                pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, false, false);
                departing.Add(pawn);
            }

            if (departing.Count > 0)
            {
                LordJob lordJob = !preferBestExit && exitCell.IsValid
                    ? (LordJob)new LordJob_ExitMapNear(exitCell, urgency)
                    : new LordJob_ExitMapBest(urgency, canDig: false, canDefendSelf: false);
                LordMaker.MakeNewLord(null, lordJob, map, departing);
            }

            ticksUntilDeparture = 0;
            if (migrationOutcomeFailed || killWindowTicksRemaining <= 0)
            {
                AbandonMigrationTracking();
            }
        }

        private void AbandonMigrationTracking()
        {
            migrationPawns.Clear();
            nextGrazeTicks.Clear();
            playerKilledMigrationPawnIds.Clear();
            ticksUntilDeparture = 0;
            exitCell = IntVec3.Invalid;
            ResetMigrationOutcome();
        }

        private void ResolveMigrationOutcome()
        {
            bool summonPunisher = !migrationOutcomeFailed
                && killWindowTicksRemaining > 0
                && originalMigrationCount > 0
                && killedMigrationPawns == originalMigrationCount;

            ResetMigrationOutcome();
            if (summonPunisher)
            {
                MapComponent_MugirlPunisher.TrySpawnPunisher(map);
            }
        }

        private void ResetMigrationOutcome()
        {
            killWindowTicksRemaining = 0;
            originalMigrationCount = 0;
            killedMigrationPawns = 0;
            playerKilledMigrationPawnIds.Clear();
            migrationOutcomeFailed = false;
            departureStarted = false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref migrationPawns, "migrationPawns", LookMode.Reference);
            Scribe_Collections.Look(ref nextGrazeTicks, "nextGrazeTicks", LookMode.Value);
            Scribe_Collections.Look(ref playerKilledMigrationPawnIds, "playerKilledMigrationPawnIds", LookMode.Value);
            Scribe_Values.Look(ref exitCell, "exitCell", IntVec3.Invalid);
            Scribe_Values.Look(ref ticksUntilDeparture, "ticksUntilDeparture", 0);
            Scribe_Values.Look(ref killWindowTicksRemaining, "killWindowTicksRemaining", 0);
            Scribe_Values.Look(ref originalMigrationCount, "originalMigrationCount", 0);
            Scribe_Values.Look(ref killedMigrationPawns, "killedMigrationPawns", 0);
            Scribe_Values.Look(ref migrationOutcomeFailed, "migrationOutcomeFailed", false);
            Scribe_Values.Look(ref departureStarted, "departureStarted", false);
            Scribe_Values.Look(ref dataVersion, "dataVersion", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (migrationPawns == null)
                {
                    migrationPawns = new List<Pawn>();
                }

                if (playerKilledMigrationPawnIds == null)
                {
                    playerKilledMigrationPawnIds = new List<int>();
                }

                EnsureGrazeTicksAligned();
                if (dataVersion < CurrentDataVersion)
                {
                    ReleaseLegacyBikiniLocks();
                    if (dataVersion < 2)
                    {
                        migrationOutcomeFailed = true;
                    }
                    dataVersion = CurrentDataVersion;
                }
            }
        }
    }
}
