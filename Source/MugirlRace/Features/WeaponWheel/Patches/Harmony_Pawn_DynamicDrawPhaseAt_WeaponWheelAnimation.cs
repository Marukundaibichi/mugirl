using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

[HarmonyPatch(typeof(Pawn), nameof(Pawn.DynamicDrawPhaseAt))]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.High, "Equip, switch and flash animations are not drawn.")]
public static class Harmony_Pawn_DynamicDrawPhaseAt_WeaponWheelAnimation
{
    [HarmonyPriority(Priority.Normal - 2)]
    public static void Postfix(Pawn __instance, DrawPhase phase)
    {
        // 三个阶段中只有主线程的 Draw 需要绘制；先判断阶段，ParallelPreDraw 与 EnsureInitialized 不查组件。
        if (phase != DrawPhase.Draw)
        {
            return;
        }

        CompWeaponWheel? comp = WeaponWheelLookup.CompFor(__instance);
        if (comp != null)
        {
            WeaponWheelAnimationRenderer.Draw(__instance, phase, comp);
        }
    }
}
