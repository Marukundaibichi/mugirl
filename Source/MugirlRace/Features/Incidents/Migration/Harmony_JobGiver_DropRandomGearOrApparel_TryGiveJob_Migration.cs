using HarmonyLib;
using Verse.AI;

namespace Mugirl.Features.Incidents;

// 原版野人工作会随机丢下装备；迁徙雪牛娘不应把比基尼扔在地上。目标是 protected 方法，只能按名字并写明参数类型。
[HarmonyPatch(typeof(JobGiver_DropRandomGearOrApparel), "TryGiveJob", new[] { typeof(Pawn) })]
[MugirlPatch(PatchModule.Incidents, PatchRisk.Medium, "Migration Mugirls can receive the vanilla wild-man job that randomly drops their bikini on the ground.")]
internal static class Harmony_JobGiver_DropRandomGearOrApparel_TryGiveJob_Migration
{
    public static bool Prefix(Pawn pawn, ref Job? __result)
    {
        if (!EventPawnTags.IsMigrationPawn(pawn))
        {
            return true;
        }

        __result = null;
        return false;
    }
}
