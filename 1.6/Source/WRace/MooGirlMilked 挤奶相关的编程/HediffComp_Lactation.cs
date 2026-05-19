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

        public CompProperties_Lactation Props => (CompProperties_Lactation)props;

        public float ProductionMultiplier
        {
            get
            {
                float multiplier = Props.baseProductionMultiplier;

                if (lastBirthTick > 0)
                {
                    float daysSinceBirth = (Find.TickManager.TicksGame - lastBirthTick) / 60000f;
                    if (daysSinceBirth < Props.postpartumBoostDays)
                    {
                        multiplier *= Props.postpartumMultiplier;
                    }
                }

                if (Pawn.needs?.food?.CurCategory == HungerCategory.UrgentlyHungry ||
                    Pawn.needs?.food?.CurCategory == HungerCategory.Starving)
                {
                    multiplier *= Props.malnourishedPenalty;
                }

                return multiplier;
            }
        }

        public void NotifyBirth()
        {
            lastBirthTick = Find.TickManager.TicksGame;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref lastBirthTick, "lastBirthTick", -1f);
        }
    }
}
