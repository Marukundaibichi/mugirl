using RimWorld;
using UnityEngine;
using Verse;

namespace MooGirl
{
    // 哺乳期 HediffComp：管理雪牛娘的哺乳状态，影响产奶速度
    public class CompProperties_Lactation : HediffCompProperties
    {
        public CompProperties_Lactation()
        {
            compClass = typeof(HediffComp_Lactation);
        }

        public float baseProductionMultiplier = 1.0f;
        public float postpartumBoostDays = 7f;
        public float postpartumMultiplier = 2.5f;
        public float malnourishedPenalty = 0.3f;
        public HediffDef breastfedBuff;
    }

    public class HediffComp_Lactation : HediffComp
    {
        private float lastBirthTick = -1f;

        public CompProperties_Lactation Props => props as CompProperties_Lactation;

        public float ProductionMultiplier
        {
            get
            {
                CompProperties_Lactation lactationProps = Props;
                if (lactationProps == null)
                {
                    return 1f;
                }

                float multiplier = Mathf.Max(0f, lactationProps.baseProductionMultiplier);

                if (lastBirthTick > 0f && MooGirlTickUtility.TryGetCurrentGameTick(out int currentTick))
                {
                    float daysSinceBirth = Mathf.Max(0f, (currentTick - lastBirthTick) / GenDate.TicksPerDay);
                    if (daysSinceBirth < lactationProps.postpartumBoostDays)
                    {
                        multiplier *= Mathf.Max(0f, lactationProps.postpartumMultiplier);
                    }
                }

                Pawn pawn = Pawn;
                HungerCategory? hungerCategory = pawn?.needs?.food?.CurCategory;
                if (hungerCategory == HungerCategory.UrgentlyHungry ||
                    hungerCategory == HungerCategory.Starving)
                {
                    multiplier *= Mathf.Max(0f, lactationProps.malnourishedPenalty);
                }

                return multiplier;
            }
        }

        public void NotifyBirth()
        {
            if (!MooGirlTickUtility.TryGetCurrentGameTick(out int currentTick))
            {
                return;
            }

            lastBirthTick = currentTick;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref lastBirthTick, "lastBirthTick", -1f);
        }
    }

    internal static class MooGirlLactationUtility
    {
        internal static void NotifyBirth(Pawn mother)
        {
            if (!MooGirlIdentity.IsMooGirlPawn(mother) || mother.Destroyed || mother.health?.hediffSet == null)
            {
                return;
            }

            HediffDef lactationDef = MooGirlRequiredDefs.Hediffs.MooGirlLactation;
            if (lactationDef == null)
            {
                return;
            }

            Hediff lactationHediff = mother.health.hediffSet.GetFirstHediffOfDef(lactationDef);
            if (lactationHediff == null)
            {
                lactationHediff = mother.health.AddHediff(lactationDef);
            }

            lactationHediff?.TryGetComp<HediffComp_Lactation>()?.NotifyBirth();
        }
    }
}
