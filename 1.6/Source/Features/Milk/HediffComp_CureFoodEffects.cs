using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MooGirl
{
    public class HediffCompProperties_CureFoodEffects : HediffCompProperties
    {
        public List<string> removeHediffs = new List<string>();

        public HediffCompProperties_CureFoodEffects()
        {
            compClass = typeof(HediffComp_CureFoodEffects);
        }
    }

    public class HediffComp_CureFoodEffects : HediffComp
    {
        private bool applied;

        public HediffCompProperties_CureFoodEffects CureProps => (HediffCompProperties_CureFoodEffects)props;

        public void ReapplyCure()
        {
            applied = false;
            ApplyCure();
        }

        public override void CompPostMake()
        {
            base.CompPostMake();
            ApplyCure();
        }

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            if (!applied) ApplyCure();
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (!applied) ApplyCure();
            if (Pawn != null && CureProps.removeHediffs != null && CureProps.removeHediffs.Count > 0)
            {
                RemoveTargetHediffs();
            }
        }

        private void ApplyCure()
        {
            if (applied) return;
            if (Pawn == null || CureProps.removeHediffs == null) return;
            applied = true;
            RemoveTargetHediffs();
        }

        private void RemoveTargetHediffs()
        {
            MooGirlFoodEffectUtility.RemoveHediffs(Pawn, CureProps.removeHediffs);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref applied, "applied", false);
        }
    }

    public class IngestionOutcomeDoer_RemoveHediffs : IngestionOutcomeDoer
    {
        public List<string> removeHediffs = new List<string>();

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            MooGirlFoodEffectUtility.RemoveHediffs(pawn, removeHediffs);
        }
    }

    public class IngestionOutcomeDoer_AddOrRefreshHediff : IngestionOutcomeDoer
    {
        public HediffDef hediffDef;
        public float severity = -1f;

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            MooGirlFoodEffectUtility.AddOrRefreshHediff(pawn, hediffDef, severity);
        }
    }
}
