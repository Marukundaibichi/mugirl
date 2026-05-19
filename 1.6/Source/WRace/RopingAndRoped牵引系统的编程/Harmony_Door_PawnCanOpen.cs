using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MooGirl
{
    [HarmonyPatch(typeof(Building_Door), nameof(Building_Door.PawnCanOpen))]
    public static class Building_Door_PawnCanOpen_Patch
    {
        [HarmonyPostfix]
        public static void AllowFollowRoperJob(Pawn p, ref bool __result)
        {
            // 如果已经允许开门，则不做处理
            if (__result)
                return;

            // 如果 pawn 正在执行 Job_FollowRoper，则允许开门
            if (p.CurJobDef == MooGirl_DefOf.Job_FollowRoper)
            {
                __result = true;
            }
        }
    }
}
