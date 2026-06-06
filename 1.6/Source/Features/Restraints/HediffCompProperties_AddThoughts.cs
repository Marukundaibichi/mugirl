using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    public class CompProperties_AddThought : HediffCompProperties
    {
        public CompProperties_AddThought()
        {
            compClass = typeof(HediffComp_AddThought);
        }

        // 多少 tick 触发
        public int triggerTicks = 600;

        // 要添加的 ThoughtDef
        public ThoughtDef thoughtDef;

    }

    public class HediffComp_AddThought : HediffComp
    {
        private int age = 0;
        private bool triggered = false;

        public CompProperties_AddThought Props => (CompProperties_AddThought)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            age++;

            if (!triggered && age >= Props.triggerTicks)
            {
                if (Pawn != null && Pawn.Spawned && Pawn.needs?.mood != null && Pawn.IsWearingCrackedBrainwashApparel())
                {
                    if (Pawn.Faction == Faction.OfPlayer && Pawn.IsColonist)
                    {
                        TryAddThought();
                        triggered = true;
                    }
                }
            }
        }

        private void TryAddThought()
        {
            if (Props.thoughtDef != null)
            {
                Pawn.needs.mood.thoughts.memories.TryGainMemory(Props.thoughtDef);
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
