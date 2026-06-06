using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MooGirl
{
    [HarmonyPatch(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.GrowthPointsPerDay), MethodType.Getter)]
    public static class Harmony_MooGirlNurtureGrowthPoints
    {
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_AgeTracker), "pawn");

        public static void Postfix(Pawn_AgeTracker __instance, ref float __result)
        {
            if (__result <= 0f)
            {
                return;
            }

            Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
            if (pawn?.health?.hediffSet?.HasHediff(MooGirlNurtureUtility.NurtureAfterglowDef) == true)
            {
                __result *= 2.5f;
                return;
            }

            Hediff nurture = pawn?.health?.hediffSet?.GetFirstHediffOfDef(MooGirlNurtureUtility.MotherlyNurtureDef);
            if (nurture == null)
            {
                return;
            }

            __result *= GrowthFactorForSeverity(nurture.Severity);
        }

        private static float GrowthFactorForSeverity(float severity)
        {
            if (severity >= 0.70f)
            {
                return 2.5f;
            }

            if (severity >= 0.40f)
            {
                return 2f;
            }

            return 1.5f;
        }
    }
}
