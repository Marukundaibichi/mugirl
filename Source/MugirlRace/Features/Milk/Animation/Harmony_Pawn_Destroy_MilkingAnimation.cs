using HarmonyLib;

namespace Mugirl.Features.Milk;

// Pawn 销毁时清除与其相关的挤奶动画状态。已在地图上的 Pawn 销毁时会先经过 DeSpawn，再次清理无副作用。
[HarmonyPatch(typeof(Pawn), nameof(Pawn.Destroy))]
[MugirlPatch(PatchModule.Milk, PatchRisk.Medium, "Milking animation state cleanup waits for other cleanup paths.")]
public static class Harmony_Pawn_Destroy_MilkingAnimation
{
    private static void Prefix(Pawn __instance)
    {
        MilkingAnimation.NotifyPawnLifecycleEnded(__instance);
    }
}
