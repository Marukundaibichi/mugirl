using HarmonyLib;

namespace Mugirl.Features.Lances;

// 1.7 原版 Pawn_EquipmentTracker.GetGizmos 仍只产出 CompEquippable 的指令，不会转发其他 ThingComp，所以由这里追加骑枪指令。
[HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.GetGizmos))]
[MugirlPatch(PatchModule.Lances, PatchRisk.Low, "骑枪冲锋按钮缺失；原有装备指令不受影响。")]
public static class Harmony_Pawn_EquipmentTracker_GetGizmos_LanceCharge
{
    public static void Postfix(Pawn_EquipmentTracker __instance, ref IEnumerable<Gizmo> __result)
    {
        CompLanceCharge? lance = __instance.Primary?.TryGetComp<CompLanceCharge>();
        if (lance is not null)
        {
            __result = AppendLanceGizmos(__result, lance);
        }
    }

    private static IEnumerable<Gizmo> AppendLanceGizmos(IEnumerable<Gizmo> original, CompLanceCharge lance)
    {
        foreach (Gizmo gizmo in original)
        {
            yield return gizmo;
        }

        foreach (Gizmo gizmo in lance.GetEquippedGizmos())
        {
            yield return gizmo;
        }
    }
}
