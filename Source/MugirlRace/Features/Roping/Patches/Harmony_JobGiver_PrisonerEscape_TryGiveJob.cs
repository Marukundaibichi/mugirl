using HarmonyLib;

namespace Mugirl.Features.Roping;

// 被拴在固定点或正在系绳的囚犯不触发逃跑任务（跳过原方法时结果为 null）。
// 跳过原方法是有意的：原方法会发送越狱消息并强制正常速度。
[HarmonyPatch(typeof(JobGiver_PrisonerEscape), "TryGiveJob")]
[MugirlPatch(PatchModule.Roping, PatchRisk.High, "Roped prisoners can receive the base escape job.")]
public static class Harmony_JobGiver_PrisonerEscape_TryGiveJob
{
    public static bool Prefix(Pawn pawn)
    {
        return !RopingService.IsBlockedFromEscape(pawn);
    }
}
