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

        public MooGirl_GameComp() { }

        public MooGirl_GameComp(Game game) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            NormalizeJuvenileGraphics();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            NormalizeJuvenileGraphics();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            NormalizeJuvenileGraphics();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (Current.ProgramState != ProgramState.Playing) return;

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
        }

        private static void NormalizeJuvenileGraphics()
        {
            int changed = MooGirlJuvenileGraphicUtility.NormalizeLoadedPawns();
            if (changed > 0)
            {
                Log.Message("[MooGirl] Corrected juvenile MooGirl body types: " + changed + ".");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref MooGirl_Structural_crash_mission_variables, "MooGirl_Structural_crash_mission_variables");
            Scribe_Values.Look(ref courierRaidTriggered, "courierRaidTriggered", false);
            Scribe_Values.Look(ref courierRaidTimer, "courierRaidTimer", CourierRaidInitialDelayTicks);
            Scribe_Values.Look(ref courierRaidQuestStarted, "courierRaidQuestStarted", false);
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
