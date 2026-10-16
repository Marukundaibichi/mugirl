using HarmonyLib;

namespace Mugirl.Features.Incidents;

// 招募成功后只清理事件与动物式状态；野人行为资格由 PawnKind 与阵营共同决定。
// 派系变化时 SetFaction 补丁已经清理过；本补丁覆盖已经是玩家派系（奴隶）时 Recruit 跳过 SetFaction 的情况。
[HarmonyPatch(typeof(RecruitUtility), nameof(RecruitUtility.Recruit))]
[MugirlPatch(PatchModule.Incidents, PatchRisk.Medium, "Escape wild-slave cleanup after recruitment is skipped.")]
internal static class Harmony_RecruitUtility_Recruit_WildMugirl
{
    public static void Postfix(Pawn pawn, Faction faction)
    {
        if (pawn != null && PlayerFactionUtility.IsPlayerFaction(faction))
        {
            WildMugirlUtility.CleanupAfterJoiningPlayer(pawn);
        }
    }
}
