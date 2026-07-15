using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace Mugirl
{
    public class MugirlStoryState : GameComponent
    {
        public const int OpeningCrashInitialCheckTicks = 360;
        public const int CourierRaidInitialDelayTicks = 5 * 60000;

        public bool openingCrashStarted;
        public int openingCrashCheckTimer = OpeningCrashInitialCheckTicks;

        public bool courierRaidTriggered;
        public int courierRaidTimer = CourierRaidInitialDelayTicks;
        public bool courierRaidQuestStarted;

        public bool fusionInvestmentAccepted;
        public bool fusionInvestmentPending;
        public int fusionInvestmentAmount;
        public int fusionInvestmentTimer;
        public int fusionInvestmentNextOfferTick;
        public bool fusionInvestmentInvestorActive;
        public Pawn fusionInvestmentInvestor;

        internal int storyServiceLastTick = -1;
        internal int storyServiceNextTick = -1;
        internal int fusionInvestmentNextStaleCheckTick = -1;

        public MugirlStoryState() { }

        public MugirlStoryState(Game game) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            ResetStoryServiceSchedule();
            ResetTransientRuntimeState();
            NormalizeJuvenileGraphics();
        }

        public override void StartedNewGame()
        {
            base.StartedNewGame();
            ResetStoryServiceSchedule();
            ResetTransientRuntimeState();
            NormalizeJuvenileGraphics();
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            ResetStoryServiceSchedule();
            ResetTransientRuntimeState();
            NormalizeJuvenileGraphics();
            MugirlEventUtility.ClearPlayerMigrationPawns();
            MugirlWildSlaveUtility.NormalizeLoadedPlayerPawnKinds();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            MugirlStoryService.Tick(this);
        }

        private static void NormalizeJuvenileGraphics()
        {
            int changed = LifeStageVisualService.NormalizeLoadedPawns();
            if (changed > 0)
            {
                MugirlLog.DevMessage("Mugirl.Newborn.Log.CorrectedBodyTypes".Translate(changed).ToString());
            }
        }

        private static void ResetTransientRuntimeState()
        {
            MugirlLog.ResetOnceWarnings();
            MugirlFoodEffectUtility.ResetDefCache();
            MugirlNurtureUtility.ResetDefCache();
            MugirlMilkingAnimation.ResetTransientState();
            MountedCombatController.ResetTransientState();
            GhoulRenderingRefreshUtility.ClearPendingRefreshes();
        }

        private void ResetStoryServiceSchedule()
        {
            storyServiceLastTick = -1;
            storyServiceNextTick = -1;
            fusionInvestmentNextStaleCheckTick = -1;
        }

        internal void WakeStoryService()
        {
            storyServiceNextTick = -1;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref openingCrashStarted, "openingCrashStarted", false);
            Scribe_Values.Look(ref openingCrashCheckTimer, "openingCrashCheckTimer", OpeningCrashInitialCheckTicks);
            Scribe_Values.Look(ref courierRaidTriggered, "courierRaidTriggered", false);
            Scribe_Values.Look(ref courierRaidTimer, "courierRaidTimer", CourierRaidInitialDelayTicks);
            Scribe_Values.Look(ref courierRaidQuestStarted, "courierRaidQuestStarted", false);
            Scribe_Values.Look(ref fusionInvestmentAccepted, "fusionInvestmentAccepted", false);
            Scribe_Values.Look(ref fusionInvestmentPending, "fusionInvestmentPending", false);
            Scribe_Values.Look(ref fusionInvestmentAmount, "fusionInvestmentAmount", 0);
            Scribe_Values.Look(ref fusionInvestmentTimer, "fusionInvestmentTimer", 0);
            Scribe_Values.Look(ref fusionInvestmentNextOfferTick, "fusionInvestmentNextOfferTick", 0);
            Scribe_Values.Look(ref fusionInvestmentInvestorActive, "fusionInvestmentInvestorActive", false);
            Scribe_References.Look(ref fusionInvestmentInvestor, "fusionInvestmentInvestor");
        }
    }

    internal static class MugirlStoryService
    {
        private const int OpeningCrashRetryTicks = 1000;
        private const float OpeningCrashQuestPoints = 10000f;
        private const float CourierRaidQuestPoints = 200f;
        private const int CourierRaidRetryTicks = 60000;
        private const int FusionInvestmentStaleCheckIntervalTicks = 250;

        internal static void Tick(MugirlStoryState state)
        {
            if (state == null || !MugirlGameUtility.IsPlaying())
            {
                return;
            }

            if (!MugirlTickUtility.TryGetCurrentGameTick(out int currentTick))
            {
                return;
            }

            EnsureScheduleInitialized(state, currentTick);
            if (state.storyServiceNextTick > currentTick)
            {
                return;
            }

            int elapsedTicks = currentTick - state.storyServiceLastTick;
            if (elapsedTicks <= 0)
            {
                elapsedTicks = 1;
            }
            state.storyServiceLastTick = currentTick;

            bool checkFusionInvestor = ShouldCheckFusionInvestor(state, currentTick);

            TickOpeningCrash(state, elapsedTicks);
            TickCourierRaid(state, elapsedTicks);
            MugirlFusionInvestmentUtility.Tick(state, elapsedTicks, checkFusionInvestor);
            ScheduleNextWake(state, currentTick);
        }

        private static void EnsureScheduleInitialized(MugirlStoryState state, int currentTick)
        {
            if (state.storyServiceLastTick < 0)
            {
                state.storyServiceLastTick = currentTick - 1;
            }

            if (state.storyServiceNextTick < 0)
            {
                state.storyServiceNextTick = currentTick;
            }

            if (state.fusionInvestmentNextStaleCheckTick < 0)
            {
                state.fusionInvestmentNextStaleCheckTick = currentTick;
            }
        }

        private static bool ShouldCheckFusionInvestor(MugirlStoryState state, int currentTick)
        {
            if (!state.fusionInvestmentInvestorActive || state.fusionInvestmentNextStaleCheckTick > currentTick)
            {
                return false;
            }

            state.fusionInvestmentNextStaleCheckTick = SafeAddTicks(currentTick, FusionInvestmentStaleCheckIntervalTicks);
            return true;
        }

        private static void TickOpeningCrash(MugirlStoryState state, int elapsedTicks)
        {
            if (state.openingCrashStarted)
            {
                state.openingCrashCheckTimer = -1;
                return;
            }

            if (state.openingCrashCheckTimer > 0)
            {
                state.openingCrashCheckTimer -= elapsedTicks;
                if (state.openingCrashCheckTimer > 0)
                {
                    return;
                }
            }

            bool hasGiantCorpFaction = MugirlGameUtility.TryGetFirstFactionOfDef(MugirlContentDefOf.Mugirl_GiantCorporations_Hostile, out _);
            bool hasMap = MugirlGameUtility.TryResolvePlayerEventMap(out Map map);
            if (hasGiantCorpFaction && hasMap && IncidentWorker_MugirlStructuralCrashMission.ShouldExecute())
            {
                Slate slate = new Slate();
                slate.Set("points", OpeningCrashQuestPoints);
                slate.Set("map", map);
                QuestUtility.GenerateQuestAndMakeAvailable(Mugirl_DefOf.Mugirl_SlaveOpeningPodCrash, slate);
                state.openingCrashStarted = true;
                state.openingCrashCheckTimer = -1;
                return;
            }

            state.openingCrashCheckTimer = OpeningCrashRetryTicks;
        }

        private static void TickCourierRaid(MugirlStoryState state, int elapsedTicks)
        {
            if (state.courierRaidTriggered)
            {
                return;
            }

            if (state.courierRaidTimer > MugirlStoryState.CourierRaidInitialDelayTicks)
            {
                state.courierRaidTimer = MugirlStoryState.CourierRaidInitialDelayTicks;
            }

            if (state.courierRaidTimer > 0)
            {
                state.courierRaidTimer -= elapsedTicks;
                if (state.courierRaidTimer > 0)
                {
                    return;
                }
            }

            if (state.courierRaidQuestStarted)
            {
                state.courierRaidTriggered = true;
                return;
            }

            bool hasGiantCorp = MugirlGameUtility.TryGetFirstFactionOfDef(MugirlContentDefOf.Mugirl_GiantCorporations_Hostile, out _);
            bool hasMap = MugirlGameUtility.TryResolvePlayerEventMap(out Map map);
            if (!hasGiantCorp || !hasMap)
            {
                state.courierRaidTimer = CourierRaidRetryTicks;
                return;
            }

            Slate slate = new Slate();
            slate.Set("points", CourierRaidQuestPoints);
            slate.Set("map", map);
            QuestUtility.GenerateQuestAndMakeAvailable(MugirlContentDefOf.Mugirl_CourierRaid, slate);
            state.courierRaidTriggered = true;
            state.courierRaidQuestStarted = true;
        }

        private static void ScheduleNextWake(MugirlStoryState state, int currentTick)
        {
            int delayTicks = int.MaxValue;

            if (!state.openingCrashStarted)
            {
                IncludeWakeDelay(ref delayTicks, state.openingCrashCheckTimer);
            }

            if (!state.courierRaidTriggered)
            {
                IncludeWakeDelay(ref delayTicks, state.courierRaidTimer);
            }

            if (state.fusionInvestmentPending)
            {
                IncludeWakeDelay(ref delayTicks, state.fusionInvestmentTimer);
            }

            if (state.fusionInvestmentInvestorActive)
            {
                IncludeWakeDelay(ref delayTicks, state.fusionInvestmentNextStaleCheckTick - currentTick);
            }

            state.storyServiceNextTick = delayTicks == int.MaxValue
                ? int.MaxValue
                : SafeAddTicks(currentTick, delayTicks);
        }

        private static void IncludeWakeDelay(ref int currentDelayTicks, int candidateTicks)
        {
            if (candidateTicks <= 0)
            {
                candidateTicks = 1;
            }

            if (candidateTicks < currentDelayTicks)
            {
                currentDelayTicks = candidateTicks;
            }
        }

        private static int SafeAddTicks(int currentTick, int delayTicks)
        {
            return currentTick > int.MaxValue - delayTicks ? int.MaxValue : currentTick + delayTicks;
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
            if (pawn.Dead || pawn.Destroyed || MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction) || pawn.mindState == null)
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

            MugirlRescueJoinUtility.PrepareRescueJoinPawn(pawn);
            if (!pawn.Downed
                && pawn.health?.CanCrawlOrMove == true
                && (pawn.guest == null || !pawn.guest.IsPrisoner)
                && MugirlRescueJoinUtility.WasRescuedByPlayer(pawn)
                && MugirlRescueJoinUtility.TryJoinPlayer(pawn))
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

    public class IncidentWorker_MugirlStructuralCrashMission : IncidentWorker
    {
        public static bool ShouldExecute()
        {
            if (MugirlMod.Settings?.enableStructuralCrashEvent != true)
                return false;
            MugirlGameUtility.TryGetGameComponent(out MugirlStoryState comp);
            if (GenDate.DaysPassedFloat < 1f) return false;
            if (comp?.openingCrashStarted == true) return false;
            return true;
        }
    }
}
