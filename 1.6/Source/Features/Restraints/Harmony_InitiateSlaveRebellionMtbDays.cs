using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(SlaveRebellionUtility), "InitiateSlaveRebellionMtbDays")]
    public static class Patch_SlaveRebellionUtility_InitiateSlaveRebellionMtbDays
    {
        public static bool Prefix(Pawn pawn, ref float __result)
        {
            if (pawn.story?.traits.HasTrait(Mugirl_DefOf.Mugirl_BrainWashObey) == true)
            {
                __result = -1f; // 叛乱时间
                return false;
            }

            return true;
        }
    }
}
