using RimWorld;
using System.Collections.Generic;
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

        public CompProperties_AddMentalState Props => (CompProperties_AddMentalState)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            age++;

            if ( !triggered && Pawn.Faction == Faction.OfPlayer && age >= Props.mentalStateTriggerTicks && Pawn.IsWearingUncrackedBrainwashApparel())
            {
                triggered = true;
                TryStartMentalState();
            }
        }

        private void TryStartMentalState()
        {
            if (Props.mentalStateDef != null && Pawn != null && Pawn.mindState != null && Pawn.Spawned)
            {
                if (!Pawn.InMentalState)
                {
                    var handler = Pawn.mindState.mentalStateHandler;
                    bool started = handler.TryStartMentalState(Props.mentalStateDef, reason: "TriggeredByHediff".Translate(), forceWake: true);
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
