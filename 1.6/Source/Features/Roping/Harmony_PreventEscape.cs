using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(JobGiver_PrisonerEscape), "TryGiveJob")]
    public static class JobGiver_PrisonerEscape_RopedBlock_Patch
    {
        public static bool Prefix(Pawn pawn)
        {
            // 被拴点或正在拴点中的囚犯不触发逃跑任务。
            return !RopingService.IsBlockedFromEscape(pawn);
        }
    }
}
