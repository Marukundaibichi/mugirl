using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace MooGirl
{
    public class MooGirlStoryState : GameComponent
    {
        public const int OpeningCrashInitialCheckTicks = 360;
        public const int CourierRaidInitialDelayTicks = 5 * 60000;

        public bool openingCrashStarted;
        public int openingCrashCheckTimer = OpeningCrashInitialCheckTicks;

        public bool courierRaidTriggered;
        public int courierRaidTimer = CourierRaidInitialDelayTicks;
        public bool courierRaidQuestStarted;

        public MooGirlStoryState() { }

        public MooGirlStoryState(Game game) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            ResetTransientRuntimeState();
            NormalizeJuvenileGraphics();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            ResetTransientRuntimeState();
            NormalizeJuvenileGraphics();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            ResetTransientRuntimeState();
            NormalizeJuvenileGraphics();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            MooGirlStoryService.Tick(this);
        }

        private static void NormalizeJuvenileGraphics()
        {
            int changed = LifeStageVisualService.NormalizeLoadedPawns();
            if (changed > 0)
            {
                MooGirlLog.DevMessage("MooGirl.Newborn.Log.CorrectedBodyTypes".Translate(changed).ToString());
            }
        }

        private static void ResetTransientRuntimeState()
        {
            MooGirlLog.ResetOnceWarnings();
            MooGirlFoodEffectUtility.ResetDefCache();
            MooGirlNurtureUtility.ResetDefCache();
            MooGirlMilkingAnimation.ResetTransientState();
            MountedCombatController.ResetTransientState();
            GhoulRenderingRefreshUtility.ClearPendingRefreshes();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref openingCrashStarted, "openingCrashStarted", false);
            Scribe_Values.Look(ref openingCrashCheckTimer, "openingCrashCheckTimer", OpeningCrashInitialCheckTicks);
            Scribe_Values.Look(ref courierRaidTriggered, "courierRaidTriggered", false);
            Scribe_Values.Look(ref courierRaidTimer, "courierRaidTimer", CourierRaidInitialDelayTicks);
            Scribe_Values.Look(ref courierRaidQuestStarted, "courierRaidQuestStarted", false);
        }
    }

    internal static class MooGirlStoryService
    {
        private const int OpeningCrashRetryTicks = 1000;
        private const float OpeningCrashQuestPoints = 10000f;
        private const float CourierRaidQuestPoints = 200f;
        private const int CourierRaidRetryTicks = 60000;

        internal static void Tick(MooGirlStoryState state)
        {
            if (state == null || !MooGirlGameUtility.IsPlaying())
            {
                return;
            }

            TickOpeningCrash(state);
            TickCourierRaid(state);
        }

        private static void TickOpeningCrash(MooGirlStoryState state)
        {
            if (state.openingCrashStarted)
            {
                state.openingCrashCheckTimer = -1;
                return;
            }

            if (state.openingCrashCheckTimer > 0)
            {
                state.openingCrashCheckTimer--;
                return;
            }

            bool hasGiantCorpFaction = MooGirlGameUtility.TryGetFirstFactionOfDef(MooGirlContentDefOf.MooGirl_GiantCorporations_Hostile, out _);
            bool hasMap = MooGirlGameUtility.TryResolvePlayerEventMap(out Map map);
            if (hasGiantCorpFaction && hasMap && IncidentWorker_MooGirlStructuralCrashMission.ShouldExecute())
            {
                Slate slate = new Slate();
                slate.Set("points", OpeningCrashQuestPoints);
                slate.Set("map", map);
                QuestUtility.GenerateQuestAndMakeAvailable(MooGirl_DefOf.MooGirl_SlaveOpeningPodCrash, slate);
                state.openingCrashStarted = true;
                state.openingCrashCheckTimer = -1;
                return;
            }

            state.openingCrashCheckTimer = OpeningCrashRetryTicks;
        }

        private static void TickCourierRaid(MooGirlStoryState state)
        {
            if (state.courierRaidTriggered)
            {
                return;
            }

            if (state.courierRaidTimer > MooGirlStoryState.CourierRaidInitialDelayTicks)
            {
                state.courierRaidTimer = MooGirlStoryState.CourierRaidInitialDelayTicks;
            }

            state.courierRaidTimer--;
            if (state.courierRaidTimer > 0)
            {
                return;
            }

            if (state.courierRaidQuestStarted)
            {
                state.courierRaidTriggered = true;
                return;
            }

            bool hasGiantCorp = MooGirlGameUtility.TryGetFirstFactionOfDef(MooGirlContentDefOf.MooGirl_GiantCorporations_Hostile, out _);
            bool hasMap = MooGirlGameUtility.TryResolvePlayerEventMap(out Map map);
            if (!hasGiantCorp || !hasMap)
            {
                state.courierRaidTimer = CourierRaidRetryTicks;
                return;
            }

            Slate slate = new Slate();
            slate.Set("points", CourierRaidQuestPoints);
            slate.Set("map", map);
            QuestUtility.GenerateQuestAndMakeAvailable(MooGirlContentDefOf.MooGirl_CourierRaid, slate);
            state.courierRaidTriggered = true;
            state.courierRaidQuestStarted = true;
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
            if (pawn.Dead || pawn.Destroyed || MooGirlWildSlaveUtility.IsPlayerFaction(pawn.Faction) || pawn.mindState == null)
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
                && pawn.health?.CanCrawlOrMove == true
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

    public class IncidentWorker_MooGirlStructuralCrashMission : IncidentWorker
    {
        public static bool ShouldExecute()
        {
            if (MooGirlMod.Settings?.enableStructuralCrashEvent != true)
                return false;
            MooGirlGameUtility.TryGetGameComponent(out MooGirlStoryState comp);
            if (GenDate.DaysPassedFloat < 1f) return false;
            if (comp?.openingCrashStarted == true) return false;
            return true;
        }
    }
}
