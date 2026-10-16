using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

// 轮盘绘制相关补丁。DrawPos 与 DynamicDrawPhaseAt 对每个 Pawn 每帧调用（含 ParallelPreDraw 工作线程），
// 入口先做最便宜的判断再查组件。

[HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawPos), MethodType.Getter)]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.High, "Linked sword dance falls back to the default teleport tween instead of its pause, recoil, rush, overshoot and return animation.")]
public static class Harmony_Pawn_DrawPos_SwordDanceDash
{
    public static void Postfix(Pawn __instance, ref UVector3 __result)
    {
        // 全局没有冲刺时直接返回：这是模组里调用最频繁的补丁。
        if (!CompWeaponWheel.AnySwordDanceDashActive)
        {
            return;
        }

        CompWeaponWheel? comp = WeaponWheelLookup.CompFor(__instance);
        if (comp != null && comp.TryGetSwordDanceDashDrawPosition(__result, out UVector3 dashPosition))
        {
            __result = dashPosition;
        }
    }
}
