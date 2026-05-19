using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MooGirl
{
    [HarmonyPatch(typeof(WorkGiver_Warden_TakeToBed), "TakeToPreferredBedJob")]
    public static class TakeToPreferredBedJob_Patch
    {
        // Postfix 会在原方法执行后调用
        [HarmonyPostfix]
        public static void Postfix(ref Job __result, Pawn prisoner, Pawn warden)
        {
            // 如果 prisoner 绑着绳子，就不允许带去床
            if (prisoner?.roping?.HasAnyRope == true)
            {
                __result = null;
            }
        }
    }
}
