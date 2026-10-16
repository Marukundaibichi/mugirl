using HarmonyLib;

namespace Mugirl.Features.Roping;

// 牵引跟随时不显示原版束缚信息。1.7 原版该方法只是 IsPrisonerOfColony && InRestraints，结果与
// InRestraints 补丁重复；仍保留，因为小方法可能被 JIT 内联进调用方，内联副本看不到 InRestraints 的补丁。
[HarmonyPatch(typeof(RestraintsUtility), nameof(RestraintsUtility.ShouldShowRestraintsInfo))]
[MugirlPatch(PatchModule.Roping, PatchRisk.High, "Base restraint info may show while the custom roping state is active.")]
public static class Harmony_RestraintsUtility_ShouldShowRestraintsInfo
{
    public static bool Prefix(Pawn pawn, ref bool __result)
    {
        if (RopingService.IsFollowingRoper(pawn))
        {
            __result = false;
            return false;
        }

        return true;
    }
}
