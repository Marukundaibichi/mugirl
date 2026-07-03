using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Mugirl
{
    [HarmonyPatch(typeof(WorkGiver_Warden_TakeToBed), "TakeToPreferredBedJob")]
    public static class TakeToPreferredBedJob_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref Job __result, Pawn prisoner, Pawn warden)
        {
            // 绳索中的囚犯不生成带去床的工作，避免与牵引状态互相抢 job。
            if (RopingService.HasAnyRope(prisoner))
            {
                __result = null;
            }
        }
    }
}
