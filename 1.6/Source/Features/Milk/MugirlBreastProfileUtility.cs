using RimWorld;
using Verse;

namespace Mugirl
{
    internal static class MugirlBreastProfileUtility
    {
        internal static bool TryGetYieldMultiplier(Pawn pawn, out float multiplier)
        {
            if (HasSpecialBreasts(pawn))
            {
                multiplier = 1.5f;
                return true;
            }

            if (HasNormalBreasts(pawn))
            {
                multiplier = 1f;
                return true;
            }

            if (HasHediff(pawn, ExternalBreastHediffDefs.SmallBreasts))
            {
                multiplier = 0.75f;
                return true;
            }

            if (HasLargeBreasts(pawn))
            {
                multiplier = 1.25f;
                return true;
            }

            if (HasFlatBreastsOrMale(pawn))
            {
                multiplier = 0.5f;
                return true;
            }

            multiplier = 0f;
            return false;
        }

        private static bool HasSpecialBreasts(Pawn pawn)
        {
            return HasHediff(pawn, ExternalBreastHediffDefs.HugeBreasts)
                || HasHediff(pawn, ExternalBreastHediffDefs.BionicBreasts)
                || HasHediff(pawn, ExternalBreastHediffDefs.SlimeBreasts)
                || HasHediff(pawn, ExternalBreastHediffDefs.GrMuffaloMammaries);
        }

        private static bool HasNormalBreasts(Pawn pawn)
        {
            return HasHediff(pawn, ExternalBreastHediffDefs.Breasts)
                || HasHediff(pawn, ExternalBreastHediffDefs.HydraulicBreasts)
                || (pawn?.gender == Gender.Female && ExternalBreastHediffDefs.Breasts == null);
        }

        private static bool HasLargeBreasts(Pawn pawn)
        {
            return HasHediff(pawn, ExternalBreastHediffDefs.LargeBreasts)
                || HasHediff(pawn, ExternalBreastHediffDefs.ArchotechBreasts);
        }

        private static bool HasFlatBreastsOrMale(Pawn pawn)
        {
            return HasHediff(pawn, ExternalBreastHediffDefs.FlatBreasts)
                || pawn?.gender == Gender.Male;
        }

        private static bool HasHediff(Pawn pawn, HediffDef hediffDef)
        {
            return pawn?.health?.hediffSet != null
                && hediffDef != null
                && pawn.health.hediffSet.HasHediff(hediffDef, false);
        }
    }
}
