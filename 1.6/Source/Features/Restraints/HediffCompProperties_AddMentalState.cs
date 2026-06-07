using RimWorld;
using Verse;

namespace MooGirl
{
    public class CompProperties_AddMentalState : HediffCompProperties
    {
        public CompProperties_AddMentalState()
        {
            compClass = typeof(HediffComp_AddMentalState);
        }

        public int mentalStateTriggerTicks = 600;

        public MentalStateDef mentalStateDef;
    }

    public class HediffComp_AddMentalState : HediffComp
    {
        private int age = 0;
        private bool triggered = false;

        public CompProperties_AddMentalState Props => props as CompProperties_AddMentalState;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            age++;

            CompProperties_AddMentalState compProps = Props;
            Pawn pawn = Pawn;
            if (triggered || compProps == null || pawn == null)
            {
                return;
            }

            int triggerTicks = compProps.mentalStateTriggerTicks < 1 ? 1 : compProps.mentalStateTriggerTicks;
            if (!triggered
                && MooGirlWildSlaveUtility.IsPlayerFaction(pawn.Faction)
                && age >= triggerTicks
                && pawn.IsWearingUncrackedBrainwashApparel())
            {
                triggered = true;
                TryStartMentalState(pawn, compProps);
            }
        }

        private void TryStartMentalState(Pawn pawn, CompProperties_AddMentalState compProps)
        {
            if (compProps?.mentalStateDef != null && pawn?.mindState?.mentalStateHandler != null && pawn.Spawned)
            {
                if (!pawn.InMentalState)
                {
                    pawn.mindState.mentalStateHandler.TryStartMentalState(compProps.mentalStateDef, reason: "TriggeredByHediff".Translate(), forceWake: true);
                }
            }
        }
        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref age, "age", 0);
            Scribe_Values.Look(ref triggered, "triggered", false);
        }
    }
}
