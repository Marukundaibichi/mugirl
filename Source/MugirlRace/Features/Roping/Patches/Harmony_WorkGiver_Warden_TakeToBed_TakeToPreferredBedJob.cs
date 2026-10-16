using HarmonyLib;
using Verse.AI;

namespace Mugirl.Features.Roping;

// 带着绳子的囚犯不生成“带去床位”的工作，避免与牵引状态互相抢 Job。
// 保持 Postfix、不跳过原方法，其他模组对该方法的补丁照常运行。
[HarmonyPatch(typeof(WorkGiver_Warden_TakeToBed), "TakeToPreferredBedJob")]
[MugirlPatch(PatchModule.Roping, PatchRisk.Medium, "Wardens may create base take-to-bed jobs for roped prisoners.")]
public static class Harmony_WorkGiver_Warden_TakeToBed_TakeToPreferredBedJob
{
    [HarmonyPostfix]
    public static void Postfix(ref Job? __result, Pawn prisoner)
    {
        if (RopingService.HasAnyRope(prisoner))
        {
            __result = null;
        }
    }
}
