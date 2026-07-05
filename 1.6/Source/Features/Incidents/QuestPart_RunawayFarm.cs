using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Mugirl
{
    public class QuestPart_SpawnRunawayFarm : QuestPart
    {
        private const int RunawayCount = 5;
        private const int RanchHalfWidth = 13;
        private const int RanchHalfHeight = 10;

        public string inSignal;
        public Site site;
        public string protectedTargetTag;
        public List<Pawn> runawayPawns = new List<Pawn>();
        public List<Thing> hitches = new List<Thing>();
        public List<Thing> farmThings = new List<Thing>();
        public Pawn rancher;
        public Faction rancherFaction;
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
            Map map = ResolveMap(signal);
            if (map == null)
            {
                MugirlLog.WarningOnce("RunawayFarm.Spawn.NoMap", "Mugirl.RunawayFarm.Log.NoMap".Translate().ToString());
                return;
            }

            IntVec3 center = FindFarmCenter(map);
            rancherFaction = Find.FactionManager.RandomNonHostileFaction(allowHidden: false, minTechLevel: TechLevel.Medieval);
            BuildFarm(map, center);
            SpawnRancher(map, center);
            SpawnRunaways(map, center);
            spawned = true;

            MugirlGameUtility.TryReceiveLetter(
                "Mugirl.RunawayFarm.ArrivedLetterLabel".Translate(),
                "Mugirl.RunawayFarm.ArrivedLetterText".Translate(),
                LetterDefOf.NeutralEvent,
                runawayPawns.Count > 0 ? new LookTargets(runawayPawns) : new LookTargets(site));
        }

        private Map ResolveMap(Signal signal)
        {
            if (site?.HasMap == true)
            {
                return site.Map;
            }

            if (SignalArgsUtility.TryGetLookTargets(signal.args, "SUBJECT", out LookTargets targets))
            {
                GlobalTargetInfo target = targets.PrimaryTarget;
                MapParent parent = target.WorldObject as MapParent;
                if (parent?.HasMap == true)
                {
                    return parent.Map;
                }
            }

            return null;
        }

        private static IntVec3 FindFarmCenter(Map map)
        {
            if (CellFinderLoose.TryFindRandomNotEdgeCellWith(
                22,
                c => c.Standable(map)
                    && !c.Roofed(map)
                    && CellRect.CenteredOn(c, RanchHalfWidth * 2 + 3, RanchHalfHeight * 2 + 3).InBounds(map),
                map,
                out IntVec3 result))
            {
                return result;
            }

            if (CellFinderLoose.TryFindRandomNotEdgeCellWith(
                18,
                c => c.Standable(map)
                    && !c.Roofed(map),
                map,
                out result))
            {
                return result;
            }

            if (CellFinderLoose.TryFindRandomNotEdgeCellWith(
                10,
                c => c.Standable(map) && !c.Roofed(map),
                map,
                out result))
            {
                return result;
            }

            return map.Center;
        }

        private void BuildFarm(Map map, IntVec3 center)
        {
            ThingDef fenceDef = ThingDef.Named("Fence");
            ThingDef fenceGateDef = ThingDef.Named("FenceGate");
            ThingDef wallDef = ThingDefOf.Wall;
            ThingDef doorDef = ThingDef.Named("Door");
            ThingDef bedDef = ThingDef.Named("Bed");
            ThingDef tableDef = ThingDef.Named("Table1x2c");
            ThingDef chairDef = ThingDef.Named("DiningChair");
            ThingDef shelfDef = ThingDef.Named("ShelfSmall");
            ThingDef haygrassDef = ThingDef.Named("Plant_Haygrass");
            Faction buildingFaction = rancherFaction;

            ClearFarmArea(map, center);

            for (int x = -RanchHalfWidth; x <= RanchHalfWidth; x++)
            {
                for (int z = -RanchHalfHeight; z <= RanchHalfHeight; z++)
                {
                    IntVec3 cell = center + new IntVec3(x, 0, z);
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }

                    bool border = Mathf.Abs(x) == RanchHalfWidth || Mathf.Abs(z) == RanchHalfHeight;
                    if (border)
                    {
                        ThingDef borderDef = x == -4 && z == -RanchHalfHeight ? fenceGateDef : fenceDef;
                        TrackFarmThing(TrySpawnBuilding(borderDef, cell, map, buildingFaction, Rot4.North));
                    }
                    else if (cell.Standable(map) && !cell.Roofed(map) && Rand.Chance(0.45f))
                    {
                        TrackFarmThing(TrySpawnPlant(haygrassDef, cell, map));
                    }
                }
            }

            BuildRanchHouse(map, center + new IntVec3(-7, 0, 0), wallDef, doorDef, bedDef, tableDef, chairDef, shelfDef, buildingFaction);
            BuildHitchWall(map, center + new IntVec3(5, 0, -2), wallDef, buildingFaction, Rot4.East, RunawayCount);
        }

        private static void ClearFarmArea(Map map, IntVec3 center)
        {
            CellRect rect = CellRect.CenteredOn(center, RanchHalfWidth * 2 + 5, RanchHalfHeight * 2 + 5).ClipInsideMap(map);
            foreach (IntVec3 cell in rect)
            {
                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed || ShouldKeepFarmAreaThing(thing))
                    {
                        continue;
                    }

                    thing.Destroy(DestroyMode.Vanish);
                }

                map.roofGrid.SetRoof(cell, null);
            }
        }

        private static bool ShouldKeepFarmAreaThing(Thing thing)
        {
            if (thing is Pawn)
            {
                return true;
            }

            Plant plant = thing as Plant;
            return plant != null
                && plant.def?.defName?.Contains("Grass") == true;
        }

        private void BuildRanchHouse(Map map, IntVec3 center, ThingDef wallDef, ThingDef doorDef, ThingDef bedDef, ThingDef tableDef, ThingDef chairDef, ThingDef shelfDef, Faction buildingFaction)
        {
            for (int x = -4; x <= 4; x++)
            {
                for (int z = -3; z <= 3; z++)
                {
                    IntVec3 cell = center + new IntVec3(x, 0, z);
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }

                    bool border = Mathf.Abs(x) == 4 || Mathf.Abs(z) == 3;
                    if (!border)
                    {
                        continue;
                    }

                    ThingDef def = x == 4 && z == 0 ? doorDef : wallDef;
                    TrackFarmThing(TrySpawnBuilding(def, cell, map, buildingFaction, Rot4.North));
                }
            }

            TrackFarmThing(TrySpawnBuilding(bedDef, center + new IntVec3(-2, 0, -1), map, buildingFaction, Rot4.East));
            TrackFarmThing(TrySpawnBuilding(tableDef, center + new IntVec3(1, 0, 0), map, buildingFaction, Rot4.East));
            TrackFarmThing(TrySpawnBuilding(chairDef, center + new IntVec3(1, 0, -2), map, buildingFaction, Rot4.North));
            TrackFarmThing(TrySpawnBuilding(chairDef, center + new IntVec3(1, 0, 2), map, buildingFaction, Rot4.South));
            SpawnMilkShelf(map, center + new IntVec3(-3, 0, 2), shelfDef, buildingFaction, Rot4.South, 24);
            SpawnMilkShelf(map, center + new IntVec3(3, 0, 2), shelfDef, buildingFaction, Rot4.South, 24);
        }

        private void SpawnMilkShelf(Map map, IntVec3 cell, ThingDef shelfDef, Faction buildingFaction, Rot4 rot, int milkCount)
        {
            Thing shelf = TrySpawnBuilding(shelfDef, cell, map, buildingFaction, rot);
            if (shelf == null)
            {
                return;
            }

            TrackFarmThing(shelf);
            Thing milk = ThingMaker.MakeThing(Mugirl_DefOf.Mugirl_Milk);
            if (milk == null)
            {
                return;
            }

            milk.stackCount = Mathf.Min(milkCount, milk.def.stackLimit);
            if (GenPlace.TryPlaceThing(milk, shelf.Position, map, ThingPlaceMode.Direct, out Thing placedMilk))
            {
                TrackFarmThing(placedMilk);
            }
        }

        private void BuildHitchWall(Map map, IntVec3 start, ThingDef wallDef, Faction buildingFaction, Rot4 hitchRot, int count)
        {
            IntVec3 wallStep = hitchRot == Rot4.East || hitchRot == Rot4.West ? IntVec3.North : IntVec3.East;
            IntVec3 wallOffset = hitchRot.FacingCell;
            for (int i = 0; i < count; i++)
            {
                IntVec3 hitchCell = start + wallStep * i;
                IntVec3 wallCell = hitchCell + wallOffset;
                TrackFarmThing(TrySpawnBuilding(wallDef, wallCell, map, buildingFaction, Rot4.North));

                Thing hitch = TrySpawnBuilding(Mugirl_DefOf.WallRopeHitch, hitchCell, map, buildingFaction, hitchRot);
                if (hitch != null)
                {
                    hitches.Add(hitch);
                    TrackFarmThing(hitch);
                }
            }
        }

        private Thing TrySpawnBuilding(ThingDef def, IntVec3 cell, Map map, Faction faction, Rot4 rot)
        {
            if (def == null || !cell.InBounds(map) || cell.Fogged(map))
            {
                return null;
            }

            if (!def.passability.Equals(Traversability.Impassable) && !cell.Standable(map))
            {
                return null;
            }

            GenSpawn.WipeExistingThings(cell, rot, def, map, DestroyMode.Vanish);
            ThingDef stuff = def.MadeFromStuff ? ThingDefOf.WoodLog : null;
            Thing thing = ThingMaker.MakeThing(def, stuff);
            if (thing == null)
            {
                return null;
            }

            if (faction != null && thing.def.CanHaveFaction)
            {
                thing.SetFaction(faction);
            }

            return GenSpawn.Spawn(thing, cell, map, rot);
        }

        private static Thing TrySpawnPlant(ThingDef plantDef, IntVec3 cell, Map map)
        {
            if (plantDef == null || !cell.InBounds(map) || cell.GetPlant(map) != null || !plantDef.CanEverPlantAt(cell, map))
            {
                return null;
            }

            Plant plant = (Plant)ThingMaker.MakeThing(plantDef);
            GenSpawn.Spawn(plant, cell, map);
            plant.Growth = Rand.Range(0.35f, 0.9f);
            return plant;
        }

        private void SpawnRancher(Map map, IntVec3 center)
        {
            PawnKindDef kindDef = PawnKindDefOf.Villager;
            PawnGenerationRequest request = new PawnGenerationRequest(
                kindDef,
                rancherFaction,
                PawnGenerationContext.NonPlayer,
                map.Tile,
                forceGenerateNewPawn: true,
                allowDead: false,
                allowDowned: false,
                canGeneratePawnRelations: false,
                mustBeCapableOfViolence: false,
                forceAddFreeWarmLayerIfNeeded: false,
                allowGay: true,
                allowPregnant: false,
                forceRecruitable: false,
                developmentalStages: DevelopmentalStage.Adult);

            rancher = PawnGenerator.GeneratePawn(request);
            if (rancher == null)
            {
                return;
            }

            AddProtectedTag(rancher);
            IntVec3 cell = center + new IntVec3(-4, 0, 0);
            if (!CellFinder.TryFindRandomCellNear(cell, map, 4, c => c.Standable(map) && !c.Fogged(map), out cell))
            {
                cell = center;
            }

            GenSpawn.Spawn(rancher, cell, map);
            LordMaker.MakeNewLord(
                rancher.Faction,
                new LordJob_WaitForDurationThenExit(cell, 12 * 60000),
                map,
                Gen.YieldSingle(rancher));
        }

        private void SpawnRunaways(Map map, IntVec3 center)
        {
            for (int i = 0; i < RunawayCount; i++)
            {
                if (!CellFinder.TryFindRandomCellNear(center, map, 7, c => c.Standable(map) && !c.Fogged(map), out IntVec3 cell))
                {
                    cell = center;
                }

                Pawn pawn = GenerateRunaway(map.Tile);
                if (pawn == null)
                {
                    continue;
                }

                GenSpawn.Spawn(pawn, cell, map);
                AddProtectedTag(pawn);
                StartRunawayWander(pawn, center);
                runawayPawns.Add(pawn);
            }
        }

        private static Pawn GenerateRunaway(PlanetTile tile)
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
                return null;
            }

            MugirlEventUtility.WearBikiniOnly(pawn);
            MugirlEventUtility.MarkRunawayFarmPawn(pawn);
            MugirlEventUtility.PreparePassiveWildPawn(pawn);

            HediffDef panicDef = MugirlContentDefOf.Mugirl_RunawayPanic;
            if (panicDef != null && pawn.health?.hediffSet?.HasHediff(panicDef) != true)
            {
                pawn.health.AddHediff(panicDef);
            }

            return pawn;
        }

        private static void StartRunawayWander(Pawn pawn, IntVec3 center)
        {
            if (pawn?.Map == null || pawn.jobs == null)
            {
                return;
            }

            Job job = JobMaker.MakeJob(JobDefOf.GotoWander, CellFinder.RandomClosewalkCellNear(center, pawn.Map, 9));
            job.expiryInterval = Rand.RangeInclusive(420, 900);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private void TrackFarmThing(Thing thing)
        {
            if (thing == null)
            {
                return;
            }

            AddProtectedTag(thing);
            farmThings.Add(thing);
        }

        private void AddProtectedTag(Thing thing)
        {
            if (thing == null || protectedTargetTag.NullOrEmpty())
            {
                return;
            }

            if (thing.questTags == null)
            {
                thing.questTags = new List<string>();
            }

            if (!thing.questTags.Contains(protectedTargetTag))
            {
                thing.questTags.Add(protectedTargetTag);
            }
        }

        internal void ClearProtectedTag(Thing thing)
        {
            if (thing == null || protectedTargetTag.NullOrEmpty())
            {
                return;
            }

            thing.questTags?.Remove(protectedTargetTag);
        }

        public override void Cleanup()
        {
            base.Cleanup();
            if (runawayPawns != null)
            {
                for (int i = 0; i < runawayPawns.Count; i++)
                {
                    Pawn pawn = runawayPawns[i];
                    if (pawn != null && !pawn.Destroyed)
                    {
                        MugirlEventUtility.ClearRunawayPanic(pawn);
                        MugirlEventUtility.ClearTemporaryEventTags(pawn);
                        ClearProtectedTag(pawn);
                    }
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
                if (runawayPawns == null)
                {
                    runawayPawns = new List<Pawn>();
                }

                if (hitches == null)
                {
                    hitches = new List<Thing>();
                }

                if (farmThings == null)
                {
                    farmThings = new List<Thing>();
                }
            }
        }
    }

    public class QuestPart_RunawayFarmCompletion : QuestPartActivable
    {
        private const int CheckIntervalTicks = 240;

        public Site site;
        public QuestPart_SpawnRunawayFarm spawnPart;
        public string failSignal;

        private int ticksUntilNextCheck;
        private bool resolved;

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
            }
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (resolved)
            {
                return;
            }

            ticksUntilNextCheck--;
            if (ticksUntilNextCheck > 0)
            {
                RefreshRunawayMovement();
                return;
            }

            ticksUntilNextCheck = CheckIntervalTicks;
            CheckCompletion();
        }

        private void CheckCompletion()
        {
            List<Pawn> pawns = spawnPart?.runawayPawns;
            if (pawns == null || pawns.Count == 0)
            {
                return;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
                {
                    FailQuest();
                    return;
                }

                if (pawn.Map == null || site?.Map != pawn.Map)
                {
                    FailQuest();
                    return;
                }
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                if (!IsRopedToPreparedHitch(pawns[i]))
                {
                    return;
                }
            }

            SucceedQuest();
        }

        private bool IsRopedToPreparedHitch(Pawn pawn)
        {
            if (!RopingService.IsRopedToSpot(pawn) || pawn?.roping?.RopedTo.IsValid != true)
            {
                return false;
            }

            List<Thing> preparedHitches = spawnPart?.hitches;
            if (preparedHitches == null || preparedHitches.Count == 0)
            {
                return false;
            }

            IntVec3 ropedCell = pawn.roping.RopedTo.Cell;
            for (int i = 0; i < preparedHitches.Count; i++)
            {
                Thing hitch = preparedHitches[i];
                if (hitch != null && !hitch.Destroyed && hitch.Spawned && hitch.Position == ropedCell)
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshRunawayMovement()
        {
            List<Pawn> pawns = spawnPart?.runawayPawns;
            if (pawns == null)
            {
                return;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Dead || !pawn.Spawned || RopingService.HasAnyRope(pawn))
                {
                    continue;
                }

                MugirlEventUtility.EnsureBikiniOnly(pawn);
                if (pawn.CurJob == null || pawn.jobs?.curDriver == null || pawn.CurJob.def != JobDefOf.GotoWander)
                {
                    Job job = JobMaker.MakeJob(JobDefOf.GotoWander, CellFinder.RandomClosewalkCellNear(pawn.Position, pawn.Map, 12));
                    job.expiryInterval = Rand.RangeInclusive(420, 900);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }
            }
        }

        private void FailQuest()
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            if (!failSignal.NullOrEmpty())
            {
                Find.SignalManager.SendSignal(new Signal(failSignal));
            }
        }

        private void SucceedQuest()
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            Pawn reward = TryChooseRewardPawn();
            if (reward != null && JoinRewardPawn(reward))
            {
                MugirlGameUtility.TrySignalForceNormalSpeedShort();
                MugirlGameUtility.TryReceiveLetter(
                    "Mugirl.RunawayFarm.SuccessLetterLabel".Translate(),
                    "Mugirl.RunawayFarm.SuccessLetterText".Translate(reward.Named("PAWN")),
                    LetterDefOf.PositiveEvent,
                    reward);
            }

            CleanupRunaways();
            Complete();
            RemoveQuestSite();
        }

        private Pawn TryChooseRewardPawn()
        {
            List<Pawn> pawns = spawnPart?.runawayPawns;
            if (pawns == null)
            {
                return null;
            }

            List<Pawn> candidates = new List<Pawn>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null
                    && !pawn.Destroyed
                    && !pawn.Dead
                    && pawn.Spawned
                    && !MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction))
                {
                    candidates.Add(pawn);
                }
            }

            return candidates.Count == 0 ? null : candidates.RandomElement();
        }

        private void RemoveQuestSite()
        {
            if (site?.HasMap == true && !HasPlayerControlledPawn(site.Map))
            {
                Current.Game.DeinitAndRemoveMap(site.Map, notifyPlayer: true);
            }

            QuestPart_DestroyWorldObject.TryRemove(site);
        }

        private static bool HasPlayerControlledPawn(Map map)
        {
            IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            if (pawns == null)
            {
                return false;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn != null && !pawn.Destroyed && (pawn.Faction == Faction.OfPlayer || pawn.HostFaction == Faction.OfPlayer))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool JoinRewardPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return false;
            }

            MugirlEventUtility.ClearRunawayPanic(pawn);
            MugirlEventUtility.ClearTemporaryEventTags(pawn);
            MugirlEventUtility.MarkRunawayRewardPawn(pawn);
            RopingService.BreakAllRopesAndNotify(pawn);
            RopingService.ClearPendingSpotRope(pawn);
            pawn.jobs?.ClearQueuedJobs();
            if (pawn.CurJob != null)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }

            bool wasEscapeWildSlave = MugirlWildSlaveUtility.IsEscapeWildSlave(pawn);
            pawn.SetFaction(Faction.OfPlayer);
            MugirlWildSlaveUtility.NormalizeAfterJoiningPlayer(pawn, wasEscapeWildSlave);
            return MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction);
        }

        private void CleanupRunaways()
        {
            List<Pawn> pawns = spawnPart?.runawayPawns;
            if (pawns == null)
            {
                return;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Destroyed)
                {
                    continue;
                }

                MugirlEventUtility.ClearRunawayPanic(pawn);
                MugirlEventUtility.ClearTemporaryEventTags(pawn);
                spawnPart?.ClearProtectedTag(pawn);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref site, "site");
            Scribe_References.Look(ref spawnPart, "spawnPart");
            Scribe_Values.Look(ref failSignal, "failSignal");
            Scribe_Values.Look(ref ticksUntilNextCheck, "ticksUntilNextCheck", 0);
            Scribe_Values.Look(ref resolved, "resolved", false);
        }
    }
}
