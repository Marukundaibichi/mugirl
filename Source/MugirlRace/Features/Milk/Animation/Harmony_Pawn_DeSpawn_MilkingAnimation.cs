using HarmonyLib;

namespace Mugirl.Features.Milk;

// Pawn 离开地图时清除与其相关的挤奶动画状态（含搭档一方）并交还原生动画槽。
[HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
[MugirlPatch(PatchModule.Milk, PatchRisk.Medium, "Milking animation state cleanup waits for other cleanup paths.")]
public static class Harmony_Pawn_DeSpawn_MilkingAnimation
{
    private static void Prefix(Pawn __instance)
    {
        MilkingAnimation.NotifyPawnLifecycleEnded(__instance);
    }
}
