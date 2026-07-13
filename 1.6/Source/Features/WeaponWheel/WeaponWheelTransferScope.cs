using System;

namespace Mugirl.Features.WeaponWheel
{
    internal enum WeaponWheelTransferMode
    {
        None,
        InternalTransfer,
        Registration
    }

    internal readonly struct WeaponWheelTransferScope : IDisposable
    {
        // StaticCacheLifecycle: 仅覆盖当前主线程上的同步 ThingOwner 操作；Dispose 后立即恢复，且不跨 Tick、存档或游戏。
        [ThreadStatic]
        private static WeaponWheelTransferMode currentMode;

        private readonly WeaponWheelTransferMode previousMode;

        internal static WeaponWheelTransferMode CurrentMode => currentMode;
        internal static bool IsInternalTransfer => currentMode == WeaponWheelTransferMode.InternalTransfer;
        internal static bool IsWheelOperation => currentMode != WeaponWheelTransferMode.None;

        private WeaponWheelTransferScope(WeaponWheelTransferMode mode)
        {
            previousMode = currentMode;
            currentMode = mode;
        }

        internal static WeaponWheelTransferScope InternalTransfer()
        {
            return new WeaponWheelTransferScope(WeaponWheelTransferMode.InternalTransfer);
        }

        internal static WeaponWheelTransferScope Registration()
        {
            return new WeaponWheelTransferScope(WeaponWheelTransferMode.Registration);
        }

        public void Dispose()
        {
            currentMode = previousMode;
        }
    }
}
