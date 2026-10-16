using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

// 把储备武器接入原版装备流程：装备通知、质量、持有者遍历、掉落与销毁。
// Notify_EquipmentRemoved 另有骑乘战斗的前缀（Harmony_MountedCombatController），它读取 EquipmentTransferScope。

[HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentAdded))]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.Critical, "Wheel weapon transfers use ordinary equipment callbacks and may reset weapon-specific state.")]
public static class Harmony_Pawn_EquipmentTracker_Notify_EquipmentAdded_WeaponWheel
{
    // 轮盘内部主手/储备互换时跳过原版通知（及其他模组对该方法的补丁），避免重置人格武器等状态。
    public static bool Prefix()
    {
        return !EquipmentTransferScope.IsInternalTransfer;
    }

    public static void Postfix(Pawn_EquipmentTracker __instance, ThingWithComps eq)
    {
        if (!EquipmentTransferScope.IsWheelOperation)
        {
            WeaponWheelLookup.CompFor(__instance.pawn)?.NotifyExternalEquipmentAdded(eq);
        }
    }
}
