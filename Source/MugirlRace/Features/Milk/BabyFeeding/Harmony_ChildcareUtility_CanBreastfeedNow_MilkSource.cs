using HarmonyLib;

namespace Mugirl.Features.Milk;

// 雪牛娘哺乳者的即时判定改用奶量；原版判定已通过时不干预。
[HarmonyPatch(typeof(ChildcareUtility), nameof(ChildcareUtility.CanBreastfeedNow))]
[MugirlPatch(PatchModule.Milk, PatchRisk.High, "Mugirl milk sources no longer satisfy the immediate breastfeeding check.")]
public static class Harmony_ChildcareUtility_CanBreastfeedNow_MilkSource
{
    public static void Postfix(Pawn mom, ref ChildcareUtility.BreastfeedFailReason? reason, ref bool __result)
    {
        if (__result || !BabyFeedingUtility.HasMooMilkSource(mom, out _))
        {
            return;
        }

        if (BabyFeedingUtility.CanMugirlBreastfeedNow(mom, out ChildcareUtility.BreastfeedFailReason? mooReason))
        {
            reason = null;
            __result = true;
        }
        else
        {
            reason = mooReason;
        }
    }
}
