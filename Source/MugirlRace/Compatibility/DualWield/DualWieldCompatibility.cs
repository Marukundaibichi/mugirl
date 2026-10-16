using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Mugirl.Features.WeaponWheel;

namespace Mugirl.Compatibility.DualWield;

/// <summary>
/// Dual Wield（MemeGoddess.DualWield）：识别副手武器，并在轮盘切换主武器时暂时收起、之后归还副手。
/// </summary>
/// <remarks>
/// 成员按其 1.7 构建的源码核对：<c>DualWield.Ext_Pawn_EquipmentTracker.TryGetOffHandEquipment(this Pawn_EquipmentTracker, out ThingWithComps)</c>
/// 与 <c>AddOffHandEquipment(this Pawn_EquipmentTracker, ThingWithComps)</c>（返回 void），
/// <c>DualWield.Ext_ThingWithComps.IsOffHand(this ThingWithComps)</c>。
/// </remarks>
internal static class DualWieldCompatibility
{
    private delegate bool TryGetOffHandEquipmentDelegate(Pawn_EquipmentTracker equipment, out ThingWithComps? offHand);

    internal static bool Active => ModCompat.DualWield;

    internal static void RegisterHooks()
    {
        if (!Active)
        {
            return;
        }
        WeaponWheelHookSet hooks = WeaponWheelHooks.Current;
        hooks.AddEligibilityVeto("DualWield", HasOffHand);
        hooks.AddCastPassThrough("DualWield", HasOffHand);
        hooks.AddForeignEquipmentFilter("DualWield", IsOffHand);
        hooks.AddPrimarySwapGuard("DualWield", new PrimarySwapGuard());
    }

    private sealed class PrimarySwapGuard : IWeaponWheelPrimarySwapGuard
    {
        public bool TryBeginPrimarySwap(CompWeaponWheel wheel, out object? token)
        {
            token = null;
            if (!TryDetachOffHand(wheel, out ThingWithComps? offHand))
            {
                return false;
            }
            if (offHand is not null)
            {
                token = new DualWieldOffHandTransferState(wheel, offHand);
            }
            return true;
        }

        public void EndPrimarySwap(CompWeaponWheel wheel, object token) => ((DualWieldOffHandTransferState)token).Restore();
    }

    internal static bool HasOffHand(Pawn? pawn)
    {
        Pawn_EquipmentTracker? equipment = pawn?.equipment;
        try
        {
            return TryGetOffHand(equipment, out ThingWithComps? offHand)
                && offHand is not null && !offHand.Destroyed && offHand != equipment.Primary;
        }
        catch (Exception exception)
        {
            WarnOnce("TryGetOffHandEquipment", exception);
            return false;
        }
    }

    internal static bool IsOffHand(ThingWithComps? equipment)
    {
        if (equipment is null || ReflectionCache.IsOffHand is not { } isOffHand)
        {
            return false;
        }

        try
        {
            return isOffHand(equipment);
        }
        catch (Exception exception)
        {
            WarnOnce("IsOffHand", exception);
            return false;
        }
    }

    /// <summary>
    /// 把副手移入轮盘的备用武器容器。没有副手时返回 true 且 <paramref name="offHand"/> 为 null；转移失败返回 false。
    /// </summary>
    internal static bool TryDetachOffHand(CompWeaponWheel? wheel, out ThingWithComps? offHand)
    {
        offHand = null;
        Pawn_EquipmentTracker? equipment = wheel?.Pawn?.equipment;
        try
        {
            if (wheel is null || !TryGetOffHand(equipment, out offHand) || offHand is null
                || offHand.Destroyed || offHand == equipment.Primary)
            {
                offHand = null;
                return true;
            }

            using (EquipmentTransferScope.InternalTransfer())
            {
                return equipment.TryTransferEquipmentToContainer(offHand, wheel.ReserveWeapons);
            }
        }
        catch (Exception exception)
        {
            WarnOnce("DetachOffHand", exception);
            offHand = null;
            return false;
        }
    }

    internal static void RestoreOffHand(CompWeaponWheel wheel, ThingWithComps offHand)
    {
        Pawn_EquipmentTracker? equipment = wheel.Pawn?.equipment;
        if (equipment is null || offHand.Destroyed || equipment.Contains(offHand))
        {
            return;
        }

        bool removedFromReserve = wheel.ReserveWeapons?.Remove(offHand) == true;
        try
        {
            if (ReflectionCache.AddOffHandEquipment is not { } addOffHand)
            {
                throw new MissingMethodException("DualWield.Ext_Pawn_EquipmentTracker.AddOffHandEquipment");
            }

            addOffHand(equipment, offHand);
            if (equipment.Contains(offHand))
            {
                return;
            }

            throw new InvalidOperationException("Dual Wield did not restore the off-hand weapon.");
        }
        catch (Exception exception)
        {
            if (removedFromReserve && offHand.ParentHolder is null)
            {
                wheel.ReserveWeapons?.TryAdd(offHand);
            }

            MugirlLog.WarningOnce(
                "WeaponWheel.DualWieldRestoreOffHandFailed",
                () => "Weapon wheel could not restore Dual Wield's off-hand weapon after a primary swap: "
                    + exception.GetType().Name + ": " + exception.Message);
        }
    }

    private static bool TryGetOffHand([NotNullWhen(true)] Pawn_EquipmentTracker? equipment, out ThingWithComps? offHand)
    {
        offHand = null;
        if (equipment is null || ReflectionCache.TryGetOffHand is not { } tryGetOffHand)
        {
            return false;
        }

        return tryGetOffHand(equipment, out offHand);
    }

    // 1.6 在这些位置静默吞掉异常；对方 Mod 改版导致兼容失效时现在每局记录一次警告，行为（返回值）不变。
    private static void WarnOnce(string operation, Exception exception)
    {
        MugirlLog.WarningOnce(
            "Compatibility.DualWield." + operation,
            () => "Dual Wield compatibility (" + operation + ") failed: " + exception.GetType().Name + ": " + exception.Message);
    }

    private static class ReflectionCache
    {
        // StaticCacheLifecycle: 进程级；兼容补丁第一次执行时解析，只缓存委托，不持有游戏对象，无需清理。
        internal static readonly TryGetOffHandEquipmentDelegate? TryGetOffHand;
        internal static readonly Action<Pawn_EquipmentTracker, ThingWithComps>? AddOffHandEquipment;
        internal static readonly Func<ThingWithComps, bool>? IsOffHand;

        static ReflectionCache()
        {
            Type? equipmentExtensions = AccessTools.TypeByName("DualWield.Ext_Pawn_EquipmentTracker");
            if (equipmentExtensions is not null)
            {
                TryGetOffHand = CompatAccessors.StaticMethod<TryGetOffHandEquipmentDelegate>(AccessTools.Method(
                    equipmentExtensions,
                    "TryGetOffHandEquipment",
                    [typeof(Pawn_EquipmentTracker), typeof(ThingWithComps).MakeByRefType()]));
                AddOffHandEquipment = CompatAccessors.StaticMethod<Action<Pawn_EquipmentTracker, ThingWithComps>>(AccessTools.Method(
                    equipmentExtensions,
                    "AddOffHandEquipment",
                    [typeof(Pawn_EquipmentTracker), typeof(ThingWithComps)]));
            }

            Type? thingExtensions = AccessTools.TypeByName("DualWield.Ext_ThingWithComps");
            if (thingExtensions is not null)
            {
                IsOffHand = CompatAccessors.StaticMethod<Func<ThingWithComps, bool>>(
                    AccessTools.Method(thingExtensions, "IsOffHand", [typeof(ThingWithComps)]));
            }

            if (TryGetOffHand is null)
            {
                CompatAccessors.ReportUnbound("Dual Wield", "DualWield.Ext_Pawn_EquipmentTracker.TryGetOffHandEquipment");
            }

            if (AddOffHandEquipment is null)
            {
                CompatAccessors.ReportUnbound("Dual Wield", "DualWield.Ext_Pawn_EquipmentTracker.AddOffHandEquipment");
            }

            if (IsOffHand is null)
            {
                CompatAccessors.ReportUnbound("Dual Wield", "DualWield.Ext_ThingWithComps.IsOffHand");
            }
        }
    }
}
