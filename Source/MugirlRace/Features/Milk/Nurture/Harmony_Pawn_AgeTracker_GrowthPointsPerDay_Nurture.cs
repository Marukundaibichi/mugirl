using HarmonyLib;

namespace Mugirl.Features.Milk;

// 哺育中与哺育余韵期间的儿童成长点倍率。
[HarmonyPatch(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.GrowthPointsPerDay), MethodType.Getter)]
[MugirlPatch(PatchModule.Milk, PatchRisk.High, "Nurture hediffs no longer alter child growth-point gain.")]
public static class Harmony_Pawn_AgeTracker_GrowthPointsPerDay_Nurture
{
    public static void Postfix(Pawn ___pawn, ref float __result)
    {
        if (__result <= 0f)
        {
            return;
        }

        HediffSet? hediffSet = ___pawn?.health?.hediffSet;
        if (hediffSet == null)
        {
            return;
        }

        if (hediffSet.HasHediff(MilkDefOf.Mugirl_NurtureAfterglow))
        {
            __result *= NurtureMath.AfterglowGrowthFactor;
            return;
        }

        Hediff? nurture = hediffSet.GetFirstHediffOfDef(MilkDefOf.Mugirl_MotherlyNurture);
        if (nurture != null)
        {
            __result *= NurtureMath.GrowthFactorForSeverity(nurture.Severity);
        }
    }
}
