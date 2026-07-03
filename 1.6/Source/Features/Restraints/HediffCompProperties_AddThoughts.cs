using RimWorld;
using Verse;

namespace Mugirl
{
    public class CompProperties_AddThought : HediffCompProperties
    {
        public CompProperties_AddThought()
        {
            compClass = typeof(HediffComp_AddThought);
        }

        // 佩戴后等待多少 tick 再添加记忆。
        public int triggerTicks = 600;

        // 达成条件后写入的 ThoughtDef。
        public ThoughtDef thoughtDef;

    }

    public class HediffComp_AddThought : HediffComp
    {
        private int age = 0;
        private bool triggered = false;

        public CompProperties_AddThought Props => props as CompProperties_AddThought;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            age++;

            CompProperties_AddThought compProps = Props;
            if (triggered || compProps == null)
            {
                return;
            }

            int triggerTicks = compProps.triggerTicks < 1 ? 1 : compProps.triggerTicks;
            if (age >= triggerTicks)
            {
                Pawn pawn = Pawn;
                if (pawn != null && pawn.Spawned && pawn.needs?.mood != null && pawn.IsWearingCrackedBrainwashApparel())
                {
                    if (MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction) && pawn.IsColonist)
                    {
                        TryAddThought(pawn, compProps);
                        triggered = true;
                    }
                }
            }
        }

        private void TryAddThought(Pawn pawn, CompProperties_AddThought compProps)
        {
            if (compProps?.thoughtDef != null && pawn?.needs?.mood?.thoughts?.memories != null)
            {
                pawn.needs.mood.thoughts.memories.TryGainMemory(compProps.thoughtDef);
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
