using HarmonyLib;
using RimWorld;
using Verse;

namespace MooGirl
{
    [HarmonyPatch(typeof(RestraintsUtility), nameof(RestraintsUtility.InRestraints))]
    public static class Patch_RestraintsUtility_InRestraints
    {
        public static bool Prefix(Pawn pawn, ref bool __result)
        {
            // 特例：正在执行牵引任务时，不视为束缚状态。
            if (RopingService.IsFollowingRoper(pawn))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(RestraintsUtility), nameof(RestraintsUtility.ShouldShowRestraintsInfo))]
    public static class RestraintsUtility_ShouldShowRestraintsInfo_Patch
    {
        public static bool Prefix(Pawn pawn, ref bool __result)
        {
            // 牵引跟随不显示原版束缚信息，避免和自定义绳索状态重复。
            if (RopingService.IsFollowingRoper(pawn))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }
}
