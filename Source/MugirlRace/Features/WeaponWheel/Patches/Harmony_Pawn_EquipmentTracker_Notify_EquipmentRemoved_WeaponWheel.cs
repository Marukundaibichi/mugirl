using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

[HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentRemoved))]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.Critical, "Wheel weapon transfers use ordinary unequip callbacks and may reset persona or unique-weapon state.")]
public static class Harmony_Pawn_EquipmentTracker_Notify_EquipmentRemoved_WeaponWheel
{
    // 同 EquipmentAdded：轮盘内部互换时跳过原版移除通知。
    [HarmonyPriority(Priority.Normal)]
    public static bool Prefix()
    {
        return !EquipmentTransferScope.IsInternalTransfer;
    }

    public static void Postfix(Pawn_EquipmentTracker __instance, ThingWithComps eq)
    {
        if (!EquipmentTransferScope.IsWheelOperation)
        {
            WeaponWheelLookup.CompFor(__instance.pawn)?.NotifyExternalEquipmentRemoved(eq);
        }
    }
}
