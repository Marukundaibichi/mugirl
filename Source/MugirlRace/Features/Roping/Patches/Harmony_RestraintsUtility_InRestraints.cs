using HarmonyLib;

namespace Mugirl.Features.Roping;

// 正在跟随牵引者时不视为束缚状态（牵引本身已限制行动，避免和原版束缚判定叠加）。
[HarmonyPatch(typeof(RestraintsUtility), nameof(RestraintsUtility.InRestraints))]
[MugirlPatch(PatchModule.Roping, PatchRisk.High, "Following ropers is treated as base restraint state.")]
public static class Harmony_RestraintsUtility_InRestraints
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
