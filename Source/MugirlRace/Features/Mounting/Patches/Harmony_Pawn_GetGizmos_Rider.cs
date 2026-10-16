using HarmonyLib;

namespace Mugirl.Features.Mounting;

// 骑乘中的骑手不在地图上，原版 Gizmo（征召、工作等）都无意义：整组替换为“选择载具/下骑”。
[HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
[MugirlPatch(PatchModule.Mounting, PatchRisk.Medium, "Mounted-pawn command gizmos fall back to the base pawn list.")]
public static class Harmony_Pawn_GetGizmos_Rider
{
    public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
    {
        if (MountUtility.IsMounted(__instance, out CompMount? comp))
        {
            __result = MountedRiderGizmos.For(__instance, comp);
        }
    }
}
