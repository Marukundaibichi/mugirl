using RimWorld;
using Verse;

namespace MooGirl
{
    internal static class MooGirlBreastProfileUtility
    {
        internal static bool TryGetProductionDays(Pawn pawn, out float days)
        {
            if (HasSpecialBreasts(pawn))
            {
                days = 3f;
                return true;
            }

            if (HasNormalBreasts(pawn))
            {
                days = 1.2f;
                return true;
            }

            if (HasHediff(pawn, MooGirlOptionalDefs.Hediffs.SmallBreasts))
            {
                days = 1f;
                return true;
            }

            if (HasLargeBreasts(pawn))
            {
                days = 1.5f;
                return true;
            }

            if (HasFlatBreastsOrMale(pawn))
            {
                days = 0.85f;
                return true;
            }

            days = 0f;
            return false;
        }

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

            if (HasHediff(pawn, MooGirlOptionalDefs.Hediffs.SmallBreasts))
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
            return HasHediff(pawn, MooGirlOptionalDefs.Hediffs.HugeBreasts)
                || HasHediff(pawn, MooGirlOptionalDefs.Hediffs.BionicBreasts)
                || HasHediff(pawn, MooGirlOptionalDefs.Hediffs.SlimeBreasts)
                || HasHediff(pawn, MooGirlOptionalDefs.Hediffs.GrMuffaloMammaries);
        }

        private static bool HasNormalBreasts(Pawn pawn)
        {
            return HasHediff(pawn, MooGirlOptionalDefs.Hediffs.Breasts)
                || HasHediff(pawn, MooGirlOptionalDefs.Hediffs.HydraulicBreasts)
                || (pawn?.gender == Gender.Female && MooGirlOptionalDefs.Hediffs.Breasts == null);
        }

        private static bool HasLargeBreasts(Pawn pawn)
        {
            return HasHediff(pawn, MooGirlOptionalDefs.Hediffs.LargeBreasts)
                || HasHediff(pawn, MooGirlOptionalDefs.Hediffs.ArchotechBreasts);
        }

        private static bool HasFlatBreastsOrMale(Pawn pawn)
        {
            return HasHediff(pawn, MooGirlOptionalDefs.Hediffs.FlatBreasts)
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
