using HarmonyLib;
using Mugirl.Features.Appearance;

namespace Mugirl.Rendering;

// Hediff.PostAdd 的统一入口：食尸鬼化需要补刷雪牛娘图形，怀孕开始需要切换怀孕变体服装。
// 两个功能共用一个 Postfix，每次添加 Hediff 只经过一层补丁。原版 HediffWithComps 与 Hediff_Pregnant 的 PostAdd 都调用基类。
[HarmonyPatch(typeof(Hediff), nameof(Hediff.PostAdd))]
[MugirlPatch(PatchModule.Rendering, PatchRisk.Medium, "Ghoul Mugirls and pregnancy-swap apparel are not refreshed when the hediff is added; their graphics update at the next renderer refresh.")]
internal static class Harmony_Hediff_PostAdd_RenderRefresh
{
    private static void Postfix(Hediff __instance)
    {
        HediffDef? def = __instance.def;
        if (def is null)
        {
            return;
        }

        if (def == RenderingDefOf.Ghoul)
        {
            PawnRenderRefresh.NotifyPawnChanged(__instance.pawn);
        }
        else if (def == HediffDefOf.PregnantHuman)
        {
            // 受孕、移植胚胎等使怀孕 Hediff 新增时刷新穿着图形。
            PregnantApparelUtility.NotifyPregnancyChanged(__instance.pawn);
        }
    }
}
