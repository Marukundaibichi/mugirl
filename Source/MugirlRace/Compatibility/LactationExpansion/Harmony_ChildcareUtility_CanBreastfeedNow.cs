using HarmonyLib;
using Mugirl.Features.Milk;

namespace Mugirl.Compatibility.LactationExpansion;

// 这两个补丁类不加 Prepare：未启用 Lactation Expansion 时 Prefix 直接返回 true（每次一个布尔判断），
// 离线验证也能据此持续检查 ChildcareUtility 的目标签名。
// Priority.First 并排在对方之前：替换型 Prefix 返回 false 后，Harmony 3 跳过之后会改变原方法结果的 Prefix（对方的补丁）。

[HarmonyPriority(Priority.First)]
[HarmonyBefore(ModCompat.LactationExpansionHarmonyId)]
[HarmonyPatch(typeof(ChildcareUtility), nameof(ChildcareUtility.CanBreastfeedNow))]
[MugirlPatch(PatchModule.LactationExpansion, PatchRisk.High, "Lactation Expansion may evaluate Mugirl milk sources through the vanilla Lactating hediff path.")]
internal static class Harmony_ChildcareUtility_CanBreastfeedNow
{
    public static bool Prefix(Pawn mom, ref ChildcareUtility.BreastfeedFailReason? reason, ref bool __result)
    {
        if (!ModCompat.LactationExpansion || !BabyFeedingUtility.HasMooMilkSource(mom, out _))
        {
            return true;
        }

        __result = BabyFeedingUtility.CanMugirlBreastfeedNow(mom, out reason);
        return false;
    }
}
