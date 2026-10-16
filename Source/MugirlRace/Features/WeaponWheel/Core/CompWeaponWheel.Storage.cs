using Verse.AI;
using Verse.Sound;

namespace Mugirl.Features.WeaponWheel;

// 槽位与储备 ThingOwner 的操作：装填、交换、丢弃、主手切换与外部装备同步，以及背负/凹槽武器的显示查询。
public sealed partial class CompWeaponWheel
{
    public bool CanAcceptWeapon(ThingWithComps? weapon, int slotIndex, out string? reason)
    {
        return storage.CanAcceptWeapon(weapon, slotIndex, out reason);
    }

    public bool TryAttachGroundWeapon(ThingWithComps weapon, int slotIndex, out string? reason)
    {
        return storage.TryAttachGroundWeapon(weapon, slotIndex, out reason);
    }

    internal bool TryAddGeneratedReserveWeapon(ThingWithComps weapon)
    {
        return storage.TryAddGeneratedReserveWeapon(weapon);
    }

    public bool TrySwapSlots(int firstIndex, int secondIndex, out string? reason)
    {
        return storage.TrySwapSlots(firstIndex, secondIndex, out reason);
    }

    public bool TryDropSlot(int slotIndex, out string? reason)
    {
        return storage.TryDropSlot(slotIndex, out reason);
    }

    internal bool MoveActiveWeaponTo(int slotIndex)
    {
        return storage.MoveActiveWeaponTo(slotIndex);
    }

    internal bool NormalizeToPrimarySlot()
    {
        return storage.NormalizeToPrimarySlot();
    }

    // 外部（非轮盘自身）给主手装上武器：纳入 0 号槽，原 0 号武器挪到第一个空槽。
    internal void NotifyExternalEquipmentAdded(ThingWithComps? equipment)
    {
        storage.NotifyExternalEquipmentAdded(equipment);
    }

    // 外部移除了一把轮盘武器（丢弃、摧毁、转移）：结束战斗循环后清空该槽。
    internal void NotifyExternalEquipmentRemoved(ThingWithComps? equipment)
    {
        storage.NotifyExternalEquipmentRemoved(equipment);
    }

    // 普通背负节点固定映射轮盘第 1、2 格；取贴图与是否显示分开，避免手持状态令节点丢失贴图。
    // 渲染线程调用：只读，不补齐集合（集合在 Initialize/读档时已补齐）。
    internal ThingWithComps? GetBackWeapon(int displayIndex)
    {
        return renderCache.GetBackWeapon(displayIndex);
    }

    internal bool IsBackWeaponVisible(int displayIndex)
    {
        return renderCache.IsBackWeaponVisible(displayIndex);
    }

    // 自驱装弹凹槽显示轮盘第 3–6 格的武器；渲染线程调用，只读。
    internal ThingWithComps? GetAutoloadingWeapon(int displayIndex)
    {
        return renderCache.GetAutoloadingWeapon(displayIndex);
    }

    internal void HandleDropAndForbidEverything(bool keepInventoryAndEquipmentIfInBed)
    {
        storage.HandleDropAndForbidEverything(keepInventoryAndEquipmentIfInBed);
    }
}
