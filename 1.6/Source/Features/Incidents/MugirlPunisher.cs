using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Mugirl
{
    public class MapComponent_MugirlPunisher : MapComponent
    {
        private const int ActiveDurationTicks = 30000;

        private readonly List<MugirlPunisherLootBurst> lootBursts = new List<MugirlPunisherLootBurst>();
        private List<Pawn> punishers = new List<Pawn>();
        private List<int> spawnedAtTicks = new List<int>();
        private List<bool> leavingFlags = new List<bool>();

        public MapComponent_MugirlPunisher(Map map) : base(map)
        {
        }

        public bool HasActivePunisher
        {
            get
            {
                for (int i = 0; i < punishers.Count; i++)
                {
                    Pawn pawn = punishers[i];
                    if (pawn != null && !pawn.Destroyed && !pawn.Dead && pawn.Spawned && pawn.Map == map)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public static bool TrySpawnPunisher(Map map)
        {
            MapComponent_MugirlPunisher component = map?.GetComponent<MapComponent_MugirlPunisher>();
            if (component == null || component.HasActivePunisher)
            {
                return false;
            }

            if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 entryCell, map, 0f, allowFogged: true))
            {
                return false;
            }

            Pawn pawn = PawnGenerator.GeneratePawn(MugirlContentDefOf.Mugirl_Punisher, null, map.Tile);
            if (pawn == null)
            {
                return false;
            }

            Rot4 rot = Rot4.FromAngleFlat((map.Center - entryCell).AngleFlat);
            GenSpawn.Spawn(pawn, entryCell, map, rot);
            component.EnsureRegistered(pawn);

            MugirlGameUtility.TryReceiveLetter(
                "Mugirl.Punisher.LetterLabel".Translate(),
                "Mugirl.Punisher.LetterText".Translate(),
                LetterDefOf.ThreatBig,
                new TargetInfo(pawn));
            return true;
        }

        internal void EnsureRegistered(Pawn pawn)
        {
            if (pawn == null || pawn.def != MugirlContentDefOf.Mugirl_PunisherRace
                || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Map != map)
            {
                return;
            }

            int index = punishers.IndexOf(pawn);
            if (index < 0)
            {
                punishers.Add(pawn);
                spawnedAtTicks.Add(MugirlTickUtility.CurrentGameTickOrFallback(0));
                leavingFlags.Add(false);
                index = punishers.Count - 1;
            }

            EnsureStateListsAligned();
            if (leavingFlags[index])
            {
                EnsureExitLord(pawn);
            }
            else
            {
                EnsureAssaultLord(pawn);
            }
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            TickLootBursts();
            EnsureStateListsAligned();

            int currentTick = MugirlTickUtility.CurrentGameTickOrFallback(0);
            for (int i = punishers.Count - 1; i >= 0; i--)
            {
                Pawn pawn = punishers[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Map != map)
                {
                    RemovePunisherAt(i);
                    continue;
                }

                if (!leavingFlags[i] && currentTick - spawnedAtTicks[i] >= ActiveDurationTicks)
                {
                    leavingFlags[i] = true;
                    EnsureExitLord(pawn);
                }
                else if (pawn.GetLord() == null)
                {
                    if (leavingFlags[i])
                    {
                        EnsureExitLord(pawn);
                    }
                    else
                    {
                        EnsureAssaultLord(pawn);
                    }
                }
            }
        }

        public override void MapComponentDraw()
        {
            base.MapComponentDraw();
            for (int i = 0; i < lootBursts.Count; i++)
            {
                lootBursts[i].Draw();
            }
        }

        internal void AddLootBurst(MugirlPunisherLootBurst burst)
        {
            if (burst != null && !burst.Expired)
            {
                lootBursts.Add(burst);
            }
        }

        private void TickLootBursts()
        {
            for (int i = lootBursts.Count - 1; i >= 0; i--)
            {
                MugirlPunisherLootBurst burst = lootBursts[i];
                burst.Tick();
                if (burst.Expired)
                {
                    lootBursts.RemoveAt(i);
                }
            }
        }

        private static LordJob MakeAssaultLordJob(Pawn pawn)
        {
            return new LordJob_AssaultColony(
                pawn.Faction,
                canKidnap: false,
                canTimeoutOrFlee: false,
                sappers: false,
                useAvoidGridSmart: false,
                canSteal: false,
                breachers: false,
                canPickUpOpportunisticWeapons: false);
        }

        private void EnsureAssaultLord(Pawn pawn)
        {
            Lord lord = pawn.GetLord();
            if (lord?.LordJob is LordJob_AssaultColony)
            {
                return;
            }

            lord?.RemovePawn(pawn);
            pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, false, false);
            LordMaker.MakeNewLord(pawn.Faction, MakeAssaultLordJob(pawn), map, Gen.YieldSingle(pawn));
        }

        private void EnsureExitLord(Pawn pawn)
        {
            Lord lord = pawn.GetLord();
            if (lord?.LordJob is LordJob_ExitMapBest)
            {
                return;
            }

            lord?.RemovePawn(pawn);
            pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, false, false);
            LordMaker.MakeNewLord(
                pawn.Faction,
                new LordJob_ExitMapBest(LocomotionUrgency.Walk, canDig: false, canDefendSelf: false),
                map,
                Gen.YieldSingle(pawn));
        }

        private void EnsureStateListsAligned()
        {
            punishers = punishers ?? new List<Pawn>();
            spawnedAtTicks = spawnedAtTicks ?? new List<int>();
            leavingFlags = leavingFlags ?? new List<bool>();

            int currentTick = MugirlTickUtility.CurrentGameTickOrFallback(0);
            while (spawnedAtTicks.Count < punishers.Count)
            {
                spawnedAtTicks.Add(currentTick);
            }

            while (leavingFlags.Count < punishers.Count)
            {
                leavingFlags.Add(false);
            }

            if (spawnedAtTicks.Count > punishers.Count)
            {
                spawnedAtTicks.RemoveRange(punishers.Count, spawnedAtTicks.Count - punishers.Count);
            }

            if (leavingFlags.Count > punishers.Count)
            {
                leavingFlags.RemoveRange(punishers.Count, leavingFlags.Count - punishers.Count);
            }
        }

        private void RemovePunisherAt(int index)
        {
            punishers.RemoveAt(index);
            spawnedAtTicks.RemoveAt(index);
            leavingFlags.RemoveAt(index);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref punishers, "punishers", LookMode.Reference);
            Scribe_Collections.Look(ref spawnedAtTicks, "punisherSpawnedAtTicks", LookMode.Value);
            Scribe_Collections.Look(ref leavingFlags, "punisherLeavingFlags", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureStateListsAligned();
            }
        }
    }

    public class JobGiver_MugirlPunisher : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            pawn?.Map?.GetComponent<MapComponent_MugirlPunisher>()?.EnsureRegistered(pawn);
            return null;
        }
    }

    public class DamageWorker_MugirlPunisherBlow : DamageWorker_Blunt
    {
        private const float PawnDamage = 4f;
        private const float BuildingDamage = 350f;

        public override DamageResult Apply(DamageInfo dinfo, Thing thing)
        {
            Pawn instigator = dinfo.Instigator as Pawn;
            bool fromPunisher = instigator?.def == MugirlContentDefOf.Mugirl_PunisherRace;
            Pawn pawn = thing as Pawn;
            if (fromPunisher && pawn != null)
            {
                dinfo.SetAmount(PawnDamage);
            }
            else if (fromPunisher && thing is Building)
            {
                dinfo.SetAmount(BuildingDamage);
            }

            DamageResult result = base.Apply(dinfo, thing);
            if (fromPunisher && pawn != null && !pawn.Dead
                && MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction) && pawn.RaceProps.Humanlike)
            {
                HediffDef knockoutDef = MugirlContentDefOf.Mugirl_PunisherKnockout;
                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(knockoutDef);
                if (existing == null)
                {
                    pawn.health.AddHediff(knockoutDef);
                }
                else
                {
                    existing.Severity = knockoutDef.initialSeverity;
                }
            }

            return result;
        }
    }
}
