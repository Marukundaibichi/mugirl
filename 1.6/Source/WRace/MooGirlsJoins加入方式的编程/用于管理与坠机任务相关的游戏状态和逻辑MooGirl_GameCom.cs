using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MooGirl
{
    public class MooGirl_GameComp : GameComponent
    {
        private const int CourierRaidInitialDelayTicks = 5 * 60000;

        public bool MooGirl_Structural_crash_mission_variables = false;
        public int structuralCrashCheckTimer = 360;

        // Courier raid state
        public bool courierRaidTriggered = false;
        public int courierRaidTimer = CourierRaidInitialDelayTicks;
        public bool courierRaidQuestStarted = false;
        public bool courierRaidLegacyFixApplied = false;
        public bool legacySaveUpgradeApplied = false;

        public MooGirl_GameComp() { }

        public MooGirl_GameComp(Game game) { }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (Current.ProgramState != ProgramState.Playing) return;

            if (!legacySaveUpgradeApplied)
            {
                RunLegacySaveUpgradeOnce();
            }

            // --- Structural crash mission timer ---
            if (structuralCrashCheckTimer > 0)
            {
                structuralCrashCheckTimer--;
            }
            else if (structuralCrashCheckTimer == 0)
            {
                Faction giantCorpFaction = Find.FactionManager.FirstFactionOfDef(AiGenerated_DefOf.MooGirl_GiantCorporations_Hostile);
                if (giantCorpFaction != null && !MooGirl_Structural_crash_mission_variables)
                {
                    if (MooGirl_Structural_crash_missiont.ShouldExecute())
                    {
                        QuestScriptDef questDef = MooGirl_DefOf.MooGirl_SlaveOpeningPodCrash;
                        QuestUtility.GenerateQuestAndMakeAvailable(questDef, 10000.0f);
                        MooGirl_Structural_crash_mission_variables = true;
                    }
                }
                if (!MooGirl_Structural_crash_mission_variables)
                    structuralCrashCheckTimer = 1000;
                else
                    structuralCrashCheckTimer = -1;
            }

            // --- Courier raid timer ---
            if (!courierRaidTriggered)
            {
                if (courierRaidTimer > CourierRaidInitialDelayTicks)
                {
                    courierRaidTimer = CourierRaidInitialDelayTicks;
                }
                courierRaidTimer--;
                if (courierRaidTimer <= 0)
                {
                    courierRaidTriggered = true;
                    if (!courierRaidQuestStarted)
                    {
                        Faction giantCorp = Find.FactionManager.FirstFactionOfDef(AiGenerated_DefOf.MooGirl_GiantCorporations_Hostile);
                        if (giantCorp != null)
                        {
                            QuestUtility.GenerateQuestAndMakeAvailable(AiGenerated_DefOf.MooGirl_CourierRaid, 200f);
                            courierRaidQuestStarted = true;
                        }
                    }
                }
            }

            TryRecoverLegacyCourierRaidQuest();
        }

        private void RunLegacySaveUpgradeOnce()
        {
            int rescuePawnsPrepared = 0;
            bool migratedLegacyFaction = false;
            bool createdHostileFaction = false;

            try
            {
                Faction legacyFaction = MooGirl_DefOf.MooGirl_GiantCorporations != null
                    ? Find.FactionManager.FirstFactionOfDef(MooGirl_DefOf.MooGirl_GiantCorporations)
                    : null;
                FactionDef hostileFactionDef = AiGenerated_DefOf.MooGirl_GiantCorporations_Hostile;
                Faction hostileFaction = hostileFactionDef != null
                    ? Find.FactionManager.FirstFactionOfDef(hostileFactionDef)
                    : null;

                rescuePawnsPrepared = PrepareLegacyRescuePawns(legacyFaction);

                if (legacyFaction != null && hostileFaction == null && hostileFactionDef != null)
                {
                    legacyFaction.def = hostileFactionDef;
                    legacyFaction.hidden = false;
                    hostileFaction = legacyFaction;
                    migratedLegacyFaction = true;
                }

                if (hostileFaction == null && hostileFactionDef != null)
                {
                    FactionGenerator.CreateFactionAndAddToManager(hostileFactionDef);
                    createdHostileFaction = Find.FactionManager.FirstFactionOfDef(hostileFactionDef) != null;
                }

                if (migratedLegacyFaction || createdHostileFaction || rescuePawnsPrepared > 0)
                {
                    Log.Message("[MooGirl] Applied one-time legacy save upgrade. "
                        + "Migrated legacy faction: " + migratedLegacyFaction
                        + ", created hostile faction: " + createdHostileFaction
                        + ", prepared rescue pawns: " + rescuePawnsPrepared + ".");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("[MooGirl] Error while applying one-time legacy save upgrade:\n" + ex);
            }
            finally
            {
                legacySaveUpgradeApplied = true;
            }
        }

        private int PrepareLegacyRescuePawns(Faction legacyFaction)
        {
            int preparedCount = 0;
            List<Pawn> pawns = PawnsFinder.AllMapsWorldAndTemporary_Alive;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!IsLegacyRescuePawn(pawn))
                {
                    continue;
                }

                if (legacyFaction != null && pawn.Faction == legacyFaction)
                {
                    pawn.SetFaction(null);
                }

                MooGirlRescueJoinUtility.PrepareRescueJoinPawn(pawn);
                preparedCount++;

                if (pawn.Faction != Faction.OfPlayer
                    && !pawn.Downed
                    && pawn.health.CanCrawlOrMove
                    && (pawn.guest == null || !pawn.guest.IsPrisoner)
                    && MooGirlRescueJoinUtility.WasRescuedByPlayer(pawn))
                {
                    MooGirlRescueJoinUtility.TryJoinPlayer(pawn);
                }
            }

            return preparedCount;
        }

        private static bool IsLegacyRescuePawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.health?.hediffSet == null)
            {
                return false;
            }

            if (!pawn.health.hediffSet.HasHediff(MooGirl_DefOf.MooGirl_Abasia))
            {
                return false;
            }

            return pawn.def == MooGirl_DefOf.MooGirl || pawn.RaceProps?.body == MooGirl_DefOf.MooGirlBody;
        }

        private void TryRecoverLegacyCourierRaidQuest()
        {
            if (!courierRaidQuestStarted || courierRaidLegacyFixApplied || Find.QuestManager == null)
            {
                return;
            }

            List<Quest> quests = Find.QuestManager.ActiveQuestsListForReading;
            for (int i = 0; i < quests.Count; i++)
            {
                Quest quest = quests[i];
                if (quest == null || quest.root != AiGenerated_DefOf.MooGirl_CourierRaid)
                {
                    continue;
                }

                string spawnSignal = QuestSignal(quest, "CourierRaid_Spawn");
                string demandSignal = QuestSignal(quest, "CourierRaid_Demand");
                bool repairedSignals = RepairCourierRaidQuestSignals(quest, spawnSignal, demandSignal);
                QuestPart_SpawnCourier spawnPart = FindCourierSpawnPart(quest);
                bool needsCourierSpawn = spawnPart != null && (spawnPart.courier == null || spawnPart.courier.Destroyed);

                if (repairedSignals && needsCourierSpawn)
                {
                    if (quest.State == QuestState.NotYetAccepted)
                    {
                        quest.SetInitiallyAccepted();
                    }
                    quest.Initiate();
                    Find.SignalManager.SendSignal(new Signal(spawnSignal));
                    Log.Message("[MooGirl] Recovered a legacy courier raid quest that was generated but never started.");
                }

                courierRaidLegacyFixApplied = true;
                return;
            }
        }

        private static bool RepairCourierRaidQuestSignals(Quest quest, string spawnSignal, string demandSignal)
        {
            bool repaired = false;
            List<QuestPart> parts = quest.PartsListForReading;
            for (int i = 0; i < parts.Count; i++)
            {
                if (!(parts[i] is QuestPart_Delay delay) || delay.outSignalsCompleted == null)
                {
                    continue;
                }

                if (HasSignal(delay.outSignalsCompleted, "CourierRaid_Spawn"))
                {
                    repaired |= ReplaceSignal(delay.outSignalsCompleted, "CourierRaid_Spawn", spawnSignal);
                    if (delay.inSignalEnable != quest.InitiateSignal)
                    {
                        delay.inSignalEnable = quest.InitiateSignal;
                        repaired = true;
                    }
                }
                else if (HasSignal(delay.outSignalsCompleted, "CourierRaid_Demand"))
                {
                    repaired |= ReplaceSignal(delay.outSignalsCompleted, "CourierRaid_Demand", demandSignal);
                    if (delay.inSignalEnable != spawnSignal)
                    {
                        delay.inSignalEnable = spawnSignal;
                        repaired = true;
                    }
                }
            }

            QuestPart_SpawnCourier spawnPart = FindCourierSpawnPart(quest);
            if (spawnPart != null && spawnPart.inSignal != spawnSignal)
            {
                spawnPart.inSignal = spawnSignal;
                repaired = true;
            }

            QuestPart_CourierDemand demandPart = FindCourierDemandPart(quest);
            if (demandPart != null && demandPart.inSignal != demandSignal)
            {
                demandPart.inSignal = demandSignal;
                repaired = true;
            }

            return repaired;
        }

        private static string QuestSignal(Quest quest, string signal)
        {
            return "Quest" + quest.id + "." + signal;
        }

        private static bool HasSignal(List<string> signals, string signal)
        {
            for (int i = 0; i < signals.Count; i++)
            {
                string existing = signals[i];
                if (existing == signal || (existing != null && existing.EndsWith("." + signal)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ReplaceSignal(List<string> signals, string signal, string replacement)
        {
            bool replaced = false;
            for (int i = 0; i < signals.Count; i++)
            {
                string existing = signals[i];
                if ((existing == signal || (existing != null && existing.EndsWith("." + signal))) && existing != replacement)
                {
                    signals[i] = replacement;
                    replaced = true;
                }
            }
            return replaced;
        }

        private static QuestPart_SpawnCourier FindCourierSpawnPart(Quest quest)
        {
            List<QuestPart> parts = quest.PartsListForReading;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is QuestPart_SpawnCourier spawnPart)
                {
                    return spawnPart;
                }
            }
            return null;
        }

        private static QuestPart_CourierDemand FindCourierDemandPart(Quest quest)
        {
            List<QuestPart> parts = quest.PartsListForReading;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] is QuestPart_CourierDemand demandPart)
                {
                    return demandPart;
                }
            }
            return null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref MooGirl_Structural_crash_mission_variables, "MooGirl_Structural_crash_mission_variables");
            Scribe_Values.Look(ref courierRaidTriggered, "courierRaidTriggered", false);
            Scribe_Values.Look(ref courierRaidTimer, "courierRaidTimer", CourierRaidInitialDelayTicks);
            Scribe_Values.Look(ref courierRaidQuestStarted, "courierRaidQuestStarted", false);
            Scribe_Values.Look(ref courierRaidLegacyFixApplied, "courierRaidLegacyFixApplied", false);
            Scribe_Values.Look(ref legacySaveUpgradeApplied, "legacySaveUpgradeApplied", false);
        }
    }

    public class HediffCompProperties_JoinWhenRescued : HediffCompProperties
    {
        public HediffCompProperties_JoinWhenRescued()
        {
            compClass = typeof(HediffComp_JoinWhenRescued);
        }
    }

    public class HediffComp_JoinWhenRescued : HediffComp
    {
        private const int CheckInterval = 240;

        private int ticksUntilNextCheck;
        private bool joinedAlready;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (joinedAlready || parent?.pawn == null)
            {
                return;
            }

            Pawn pawn = parent.pawn;
            if (pawn.Dead || pawn.Faction == Faction.OfPlayer)
            {
                joinedAlready = true;
                return;
            }

            ticksUntilNextCheck--;
            if (ticksUntilNextCheck > 0)
            {
                return;
            }
            ticksUntilNextCheck = CheckInterval;

            MooGirlRescueJoinUtility.PrepareRescueJoinPawn(pawn);
            if (!pawn.Downed
                && pawn.health.CanCrawlOrMove
                && (pawn.guest == null || !pawn.guest.IsPrisoner)
                && MooGirlRescueJoinUtility.WasRescuedByPlayer(pawn)
                && MooGirlRescueJoinUtility.TryJoinPlayer(pawn))
            {
                joinedAlready = true;
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref ticksUntilNextCheck, "ticksUntilNextCheck", 0);
            Scribe_Values.Look(ref joinedAlready, "joinedAlready", false);
        }
    }

    public class MooGirl_Structural_crash_missiont : IncidentWorker
    {
        public static bool ShouldExecute()
        {
            if (MooGirlMod.settings?.enableStructuralCrashEvent != true)
                return false;
            var comp = Current.Game.GetComponent<MooGirl_GameComp>();
            if (GenDate.DaysPassedFloat < 1f) return false;
            if (comp?.MooGirl_Structural_crash_mission_variables == true) return false;
            return true;
        }
    }
}
