using HarmonyLib;

namespace Mugirl.Features.Mounting;

// 骑手卸下武器时注销该武器的骑乘 verb 并还原施放者；武器轮盘内部换手不算卸下。
[HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentRemoved))]
[MugirlPatch(PatchModule.Mounting, PatchRisk.High, "Mounted combat verb cleanup after equipment removal is skipped.")]
public static class Harmony_Pawn_EquipmentTracker_Notify_EquipmentRemoved_MountedVerbs
{
    [HarmonyPriority(Priority.Normal + 1)]
    public static void Prefix(ThingWithComps eq)
    {
        if (!EquipmentTransferScope.IsInternalTransfer)
        {
            MountedCombatController.NotifyEquipmentRemoved(eq);
        }
    }
}
