using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MooGirl
{
    [HarmonyPatch(typeof(RestraintsUtility), nameof(RestraintsUtility.InRestraints))]
    public static class Patch_RestraintsUtility_InRestraints
    {
        public static bool Prefix(Pawn pawn, ref bool __result)
        {
            // 特例：正在执行牵引任务时，不视为束缚状态
            if (pawn.CurJobDef == MooGirl_DefOf.Job_FollowRoper)
            {
                __result = false;
                return false; // 跳过原始方法
            }

            // 否则继续原始判断
            return true; // 执行原始方法
        }
    }

    [HarmonyPatch(typeof(RestraintsUtility), nameof(RestraintsUtility.ShouldShowRestraintsInfo))]
    public static class RestraintsUtility_ShouldShowRestraintsInfo_Patch
    {
        static bool Prefix(Pawn pawn, ref bool __result)
        {
            // 如果 pawn 执行的工作是跟随绳索（Job_FollowRoper），直接返回 false，不显示束缚信息
            if (pawn.CurJob != null && pawn.CurJob.def == MooGirl_DefOf.Job_FollowRoper)
            {
                __result = false;
                return false; // 跳过原方法
            }

            // 继续执行原方法
            return true;
        }
    }
}
