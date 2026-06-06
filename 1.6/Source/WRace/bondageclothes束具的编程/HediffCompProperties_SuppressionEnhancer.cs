using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MooGirl
{
    public class CompProperties_SuppressionEnhancer : HediffCompProperties
    {
        public CompProperties_SuppressionEnhancer()
        {
            this.compClass = typeof(HediffComp_SuppressionEnhancer);
        }


        // 多少tick生效
        public int triggerTicks = 600;

        // 是否启用压制强化
        public bool enhancedMode = false;

        // 常规压制数值
        public float regularAmount = 0.10f;

        // 加强压制数值
        public float enhancedAmount = 0.30f;
    }

    public class HediffComp_SuppressionEnhancer : HediffComp
    {
        private int age;

        public CompProperties_SuppressionEnhancer Props => (CompProperties_SuppressionEnhancer)this.props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            age++;
            if (age < Props.triggerTicks) return;

            age = 0;

            if (Pawn.IsSlaveOfColony && Pawn.IsWearingCrackedBrainwashApparel())
            {
                ApplySuppression();
            }
        }

        private void ApplySuppression()
        {
            Need_Suppression suppression = Pawn.needs?.TryGetNeed<Need_Suppression>();
            if (suppression == null) return;

            float amount = Props.enhancedMode ? Props.enhancedAmount : Props.regularAmount;
            suppression.CurLevelPercentage = Mathf.Clamp01(suppression.CurLevelPercentage + amount);

            MoteMaker.ThrowText(Pawn.DrawPos, Pawn.Map, "MooGirl.SuppressionIncrease".Translate(amount.ToStringPercent()), Color.yellow, 6f);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref age, "age", 0);
        }
    }
}
