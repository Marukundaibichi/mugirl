using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.GrowthPointsPerDay), MethodType.Getter)]
    public static class Harmony_MugirlNurtureGrowthPoints
    {
        // StaticCacheLifecycle: 进程级 Pawn_AgeTracker.pawn 反射缓存；不持有游戏对象。
        private static readonly FieldInfo PawnField = AccessTools.Field(typeof(Pawn_AgeTracker), "pawn");

        public static void Postfix(Pawn_AgeTracker __instance, ref float __result)
        {
            if (__result <= 0f)
            {
                return;
            }

            Pawn pawn = PawnField?.GetValue(__instance) as Pawn;
            HediffSet hediffSet = pawn?.health?.hediffSet;
            if (hediffSet == null)
            {
                return;
            }

            HediffDef afterglowDef = MugirlNurtureUtility.NurtureAfterglowDef;
            if (afterglowDef != null && hediffSet.HasHediff(afterglowDef))
            {
                __result *= 2.5f;
                return;
            }

            HediffDef nurtureDef = MugirlNurtureUtility.MotherlyNurtureDef;
            if (nurtureDef == null)
            {
                return;
            }

            Hediff nurture = hediffSet.GetFirstHediffOfDef(nurtureDef);
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
