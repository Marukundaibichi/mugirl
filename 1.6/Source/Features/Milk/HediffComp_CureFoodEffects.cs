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

        public HediffCompProperties_CureFoodEffects CureProps => props as HediffCompProperties_CureFoodEffects;

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
            if (TryGetRemoveHediffs(out List<string> removeHediffs) && removeHediffs.Count > 0)
            {
                RemoveTargetHediffs(Pawn, removeHediffs);
            }
        }

        private void ApplyCure()
        {
            if (applied) return;
            if (CureProps == null)
            {
                applied = true;
                return;
            }

            if (!TryGetRemoveHediffs(out List<string> removeHediffs)) return;
            applied = true;
            RemoveTargetHediffs(Pawn, removeHediffs);
        }

        private bool TryGetRemoveHediffs(out List<string> removeHediffs)
        {
            removeHediffs = CureProps?.removeHediffs;
            return Pawn?.health?.hediffSet != null && removeHediffs != null;
        }

        private void RemoveTargetHediffs(Pawn pawn, List<string> removeHediffs)
        {
            MooGirlFoodEffectUtility.RemoveHediffs(pawn, removeHediffs);
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
