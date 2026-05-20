using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MooGirl
{
    [HarmonyPatch(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.GrowthPointsPerDay), MethodType.Getter)]
    public static class Harmony_BreastfedGrowthPoints
    {
        private const float BreastfedGrowthPointsFactor = 1.5f;
        private const string BreastfedHediffDefName = "MooGirl_Breastfed";

        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_AgeTracker), "pawn");
        private static HediffDef breastfedHediffDef;

        public static void Postfix(Pawn_AgeTracker __instance, ref float __result)
        {
            if (__result <= 0f)
            {
                return;
            }

            Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
            if (pawn?.health?.hediffSet == null)
            {
                return;
            }

            if (breastfedHediffDef == null)
            {
                breastfedHediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(BreastfedHediffDefName);
            }
            if (breastfedHediffDef != null && pawn.health.hediffSet.HasHediff(breastfedHediffDef))
            {
                __result *= BreastfedGrowthPointsFactor;
            }
        }
    }
}
