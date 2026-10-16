using HarmonyLib;
using Verse.AI;

namespace Mugirl.Features.Incidents;

// 驯服失败时原版可能让野人进入狂暴；迁徙与逃跑牧场的雪牛娘是被动事件对象，不应因此暴走。
[HarmonyPatch(typeof(Pawn_MindState), "CheckStartMentalStateBecauseRecruitAttempted", new[] { typeof(Pawn) })]
[MugirlPatch(PatchModule.Incidents, PatchRisk.Medium, "Passive event pawns may start the base recruit-attempt mental state.")]
internal static class Harmony_Pawn_MindState_CheckStartMentalStateBecauseRecruitAttempted_PassiveEventPawn
{
    public static bool Prefix(Pawn_MindState __instance, ref bool __result)
    {
        if (!EventPawnTags.IsPassiveEventPawn(__instance.pawn))
        {
            return true;
        }

        __result = false;
        return false;
    }
}
