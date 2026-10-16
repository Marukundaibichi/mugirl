using HarmonyLib;

namespace Mugirl.Features.Restraints;

// Wear 会在加入新服装前卸下冲突服装。该内部卸装不应触发束具的“下一阶段”生成，
// 否则用脚铐替换同层下身束具时会凭空制造并穿上一双奴隶袜。
[HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Wear))]
[MugirlPatch(PatchModule.Restraints, PatchRisk.High, "Replacing apparel can trigger slave-apparel auto-stage callbacks and create complementary restraints from nothing.")]
public static class Harmony_Pawn_ApparelTracker_Wear
{
    public static void Prefix(out bool __state)
    {
        RestraintAutoStageContext.BeginSuppressNextStage();
        __state = true;
    }

    public static void Finalizer(bool __state)
    {
        if (__state)
        {
            RestraintAutoStageContext.EndSuppressNextStage();
        }
    }
}
