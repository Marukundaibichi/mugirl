namespace Mugirl.Core;

// 标记当前同步执行的 ThingOwner 操作来自轮盘自身，供装备通知补丁（以及骑乘战斗补丁）区分外部操作。
internal readonly struct EquipmentTransferScope : IDisposable
{
    // StaticCacheLifecycle: 仅覆盖当前线程上的同步 ThingOwner 操作；Dispose 后立即恢复，且不跨 Tick、存档或游戏。
    [ThreadStatic]
    private static EquipmentTransferMode currentMode;

    private readonly EquipmentTransferMode previousMode;

    private EquipmentTransferScope(EquipmentTransferMode mode)
    {
        previousMode = currentMode;
        currentMode = mode;
    }

    internal static bool IsInternalTransfer => currentMode == EquipmentTransferMode.InternalTransfer;

    internal static bool IsWheelOperation => currentMode != EquipmentTransferMode.None;

    internal static EquipmentTransferScope InternalTransfer() => new(EquipmentTransferMode.InternalTransfer);

    internal static EquipmentTransferScope Registration() => new(EquipmentTransferMode.Registration);

    public void Dispose()
    {
        currentMode = previousMode;
    }
}
