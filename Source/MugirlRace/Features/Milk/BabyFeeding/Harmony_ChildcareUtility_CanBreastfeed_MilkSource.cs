using HarmonyLib;

namespace Mugirl.Features.Milk;

// 原版判定失败时，带奶量的雪牛娘仍可作为哺乳者。
[HarmonyPatch(typeof(ChildcareUtility), nameof(ChildcareUtility.CanBreastfeed))]
[MugirlPatch(PatchModule.Milk, PatchRisk.High, "Mugirl milk sources no longer satisfy the base breastfeeding availability check.")]
public static class Harmony_ChildcareUtility_CanBreastfeed_MilkSource
{
    public static void Postfix(Pawn mom, ref ChildcareUtility.BreastfeedFailReason? reason, ref bool __result)
    {
        if (__result)
        {
            return;
        }

        if (BabyFeedingUtility.CanMugirlBreastfeed(mom, out _))
        {
            reason = null;
            __result = true;
        }
    }
}
