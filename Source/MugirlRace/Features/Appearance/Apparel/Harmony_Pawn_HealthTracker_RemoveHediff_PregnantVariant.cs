using HarmonyLib;

namespace Mugirl.Features.Appearance;

// 分娩、流产与终止妊娠都会经 Pawn_HealthTracker.RemoveHediff 移除怀孕 Hediff；新增怀孕由 Harmony_Hediff_PostAdd_RenderRefresh 处理。
[HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff))]
[MugirlPatch(PatchModule.Appearance, PatchRisk.Low, "怀孕结束（分娩、流产、终止妊娠）后穿着贴图不立即刷新，等到下一次图形重建才换回原贴图。")]
public static class Harmony_Pawn_HealthTracker_RemoveHediff_PregnantVariant
{
    public static void Postfix(Hediff? hediff)
    {
        if (hediff is null || HediffDefOf.PregnantHuman is null || hediff.def != HediffDefOf.PregnantHuman)
        {
            return;
        }

        PregnantApparelUtility.NotifyPregnancyChanged(hediff.pawn);
    }
}
