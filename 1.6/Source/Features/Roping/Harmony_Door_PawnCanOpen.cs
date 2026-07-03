using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(Building_Door), nameof(Building_Door.PawnCanOpen))]
    public static class Building_Door_PawnCanOpen_Patch
    {
        [HarmonyPostfix]
        public static void AllowFollowRoperJob(Pawn p, ref bool __result)
        {
            if (__result)
            {
                return;
            }

            // 跟随牵引者时保持旧行为：即使门原本拒绝，也允许打开。
            if (RopingService.IsFollowingRoper(p))
            {
                __result = true;
            }
        }
    }
}
