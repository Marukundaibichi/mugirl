using HarmonyLib;

namespace Mugirl.Features.Roping;

// 被牵引、被拴或正在系绳的雪牛娘禁用征召按钮（牵引 tick 也会自动取消征召）。
// 目标方法是 internal 迭代器，只能按名字指定。
[HarmonyPatch(typeof(Pawn_DraftController), "GetGizmos")]
[MugirlPatch(PatchModule.Roping, PatchRisk.Medium, "Draft gizmo disabling while roped is skipped.")]
public static class Harmony_Pawn_DraftController_GetGizmos
{
    [HarmonyPostfix]
    public static void GetGizmosPostfix(ref IEnumerable<Gizmo>? __result, Pawn_DraftController __instance)
    {
        Pawn? pawn = __instance.pawn;
        if (__result is null || !RopingService.IsMugirlRopee(pawn))
        {
            return;
        }

        if (!RopingService.IsRopedByPawn(pawn) && !RopingService.IsRopedToSpot(pawn) && !RopingService.IsPendingSpotRope(pawn))
        {
            return;
        }

        __result = DisableDraftGizmo(__result);
    }

    private static IEnumerable<Gizmo> DisableDraftGizmo(IEnumerable<Gizmo> gizmos)
    {
        foreach (Gizmo gizmo in gizmos)
        {
            // 原版征召按钮的图标固定为 TexCommand.Draft；按引用比较，不用 UnityStubs 的 Texture ==（按释放状态判断）。
            if (gizmo is Command_Toggle toggle && ReferenceEquals(toggle.icon, TexCommand.Draft))
            {
                toggle.Disable("Mugirl.DraftDisabledWhileRoped".Translate());
            }

            yield return gizmo;
        }
    }
}
