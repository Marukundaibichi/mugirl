using HarmonyLib;

namespace Mugirl.Features.Lances;

// 落地后的短暂运动模糊。ParallelPreDraw 阶段由工作线程调用：这里只比较 phase，绘制留在主线程的 Draw 阶段。
// 每个 Pawn 每帧都会经过：先看全局收回窗口，没有刚落地的冲锋时不逐人物查弱表。
[HarmonyPatch(typeof(Pawn), nameof(Pawn.DynamicDrawPhaseAt))]
[MugirlPatch(PatchModule.Lances, PatchRisk.Low, "Landing motion blur is omitted; pawn drawing and charge gameplay remain unchanged.")]
internal static class Harmony_Pawn_DynamicDrawPhaseAt_LanceBlurRecovery
{
    [HarmonyPriority(Priority.Normal)]
    private static void Postfix(Pawn __instance, DrawPhase phase)
    {
        if (phase == DrawPhase.Draw && LanceMotionBlur.AnyRecoveryActive())
        {
            LanceMotionBlur.DrawRecovery(__instance);
        }
    }
}
