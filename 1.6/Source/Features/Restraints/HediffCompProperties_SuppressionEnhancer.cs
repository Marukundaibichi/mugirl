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

        // 每隔一段时间补充奴隶压制度，可切换为加强模式。
        public int triggerTicks = 600;
        public bool enhancedMode = false;
        public float regularAmount = 0.10f;
        public float enhancedAmount = 0.30f;
    }

    public class HediffComp_SuppressionEnhancer : HediffComp
    {
        private int age;

        public CompProperties_SuppressionEnhancer Props => props as CompProperties_SuppressionEnhancer;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            CompProperties_SuppressionEnhancer compProps = Props;
            if (compProps == null)
            {
                return;
            }

            age++;
            int triggerTicks = Mathf.Max(1, compProps.triggerTicks);
            if (age < triggerTicks) return;

            age = 0;

            Pawn pawn = Pawn;
            if (pawn?.IsSlaveOfColony == true && pawn.IsWearingCrackedBrainwashApparel())
            {
                ApplySuppression(pawn, compProps);
            }
        }

        private void ApplySuppression(Pawn pawn, CompProperties_SuppressionEnhancer compProps)
        {
            Need_Suppression suppression = pawn?.needs?.TryGetNeed<Need_Suppression>();
            if (suppression == null) return;

            float amount = Mathf.Max(0f, compProps.enhancedMode ? compProps.enhancedAmount : compProps.regularAmount);
            if (amount <= 0f) return;

            suppression.CurLevelPercentage = Mathf.Clamp01(suppression.CurLevelPercentage + amount);

            if (pawn.Map != null)
            {
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "MooGirl.SuppressionIncrease".Translate(amount.ToStringPercent()), Color.yellow, 6f);
            }
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref age, "age", 0);
        }
    }
}
