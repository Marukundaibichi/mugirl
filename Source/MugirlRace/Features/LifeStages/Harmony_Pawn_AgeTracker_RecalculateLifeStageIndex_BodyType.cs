using HarmonyLib;

namespace Mugirl.Features.LifeStages;

// 生命阶段重算（成长每 240 tick、生日、锁定阶段、开发者调整年龄）后立即校正身形。目标是私有方法，只能按名称指定。
[HarmonyPatch(typeof(Pawn_AgeTracker), "RecalculateLifeStageIndex")]
[MugirlPatch(PatchModule.LifeStages, PatchRisk.High, "Life-stage body normalization waits for later explicit refreshes.")]
internal static class Harmony_Pawn_AgeTracker_RecalculateLifeStageIndex_BodyType
{
    // ___pawn：Harmony 在打补丁时绑定私有字段 Pawn_AgeTracker.pawn，调用时不再反射。
    public static void Postfix(Pawn? ___pawn)
    {
        LifeStageBodyTypeUtility.NormalizeBodyType(___pawn);
    }
}
