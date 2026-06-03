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
            if (pawn == null || hediffDef == null) return;

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(hediffDef, pawn);
                if (severity > 0f)
                {
                    hediff.Severity = severity;
                }
                pawn.health.AddHediff(hediff);
            }
            else if (severity > 0f && hediff.Severity < severity)
            {
                hediff.Severity = severity;
            }

            HediffComp_Disappears disappears = hediff.TryGetComp<HediffComp_Disappears>();
            if (disappears != null)
            {
                disappears.ResetElapsedTicks();
            }

            HediffComp_CureFoodEffects cure = hediff.TryGetComp<HediffComp_CureFoodEffects>();
            if (cure != null)
            {
                cure.ReapplyCure();
            }
        }
    }

    internal static class MooGirlFoodEffectUtility
    {
        public static void RemoveHediffs(Pawn pawn, List<string> removeHediffs)
        {
            if (pawn == null || removeHediffs == null || removeHediffs.Count == 0) return;

            for (int i = 0; i < removeHediffs.Count; i++)
            {
                HediffDef hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(removeHediffs[i]);
                if (hediffDef == null) continue;

                Hediff hediff;
                while ((hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef)) != null)
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }
        }
    }
}
