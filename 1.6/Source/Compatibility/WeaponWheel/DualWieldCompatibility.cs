using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mugirl.Features.WeaponWheel;
using Verse;

namespace Mugirl
{
    internal static class DualWieldCompatibility
    {
        private delegate bool TryGetOffHandEquipmentDelegate(
            Pawn_EquipmentTracker equipment,
            out ThingWithComps offHand);

        private const string PackageId = "MemeGoddess.DualWield";

        internal static bool Active => ModLister.GetActiveModWithIdentifier(PackageId) != null;

        internal static bool HasOffHand(Pawn pawn)
        {
            Pawn_EquipmentTracker equipment = pawn?.equipment;
            try
            {
                return TryGetOffHand(equipment, out ThingWithComps offHand)
                    && offHand != null && !offHand.Destroyed && offHand != equipment.Primary;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsOffHand(ThingWithComps equipment)
        {
            if (equipment == null || ReflectionCache.IsOffHandMethod == null)
            {
                return false;
            }

            try
            {
                return ReflectionCache.IsOffHandMethod.Invoke(null, new object[] { equipment }) is bool result && result;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryDetachOffHand(Comp_WeaponWheel wheel, out ThingWithComps offHand)
        {
            offHand = null;
            Pawn pawn = wheel?.Pawn;
            Pawn_EquipmentTracker equipment = pawn?.equipment;
            try
            {
                if (!TryGetOffHand(equipment, out offHand) || offHand == null
                    || offHand.Destroyed || offHand == equipment.Primary)
                {
                    offHand = null;
                    return true;
                }

                using (WeaponWheelTransferScope.InternalTransfer())
                {
                    return equipment.TryTransferEquipmentToContainer(offHand, wheel.ReserveWeapons);
                }
            }
            catch
            {
                offHand = null;
                return false;
            }
        }

        internal static void RestoreOffHand(Comp_WeaponWheel wheel, ThingWithComps offHand)
        {
            Pawn_EquipmentTracker equipment = wheel?.Pawn?.equipment;
            if (equipment == null || offHand == null || offHand.Destroyed || equipment.Contains(offHand))
            {
                return;
            }

            bool removedFromReserve = wheel.ReserveWeapons?.Remove(offHand) == true;
            try
            {
                if (ReflectionCache.AddOffHandEquipmentMethod == null)
                {
                    throw new MissingMethodException("DualWield.Ext_Pawn_EquipmentTracker.AddOffHandEquipment");
                }

                ReflectionCache.AddOffHandEquipmentMethod.Invoke(null, new object[] { equipment, offHand });
                if (equipment.Contains(offHand))
                {
                    return;
                }
                throw new InvalidOperationException("Dual Wield did not restore the off-hand weapon.");
            }
            catch (Exception ex)
            {
                if (removedFromReserve && offHand.ParentHolder == null)
                {
                    wheel.ReserveWeapons.TryAdd(offHand);
                }
                MugirlLog.WarningOnce(
                    "WeaponWheel.DualWieldRestoreOffHandFailed",
                    "Weapon wheel could not restore Dual Wield's off-hand weapon after a primary swap: "
                    + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool TryGetOffHand(Pawn_EquipmentTracker equipment, out ThingWithComps offHand)
        {
            offHand = null;
            if (equipment == null || ReflectionCache.TryGetOffHandMethod == null)
            {
                return false;
            }

            if (ReflectionCache.TryGetOffHand != null)
            {
                return ReflectionCache.TryGetOffHand(equipment, out offHand);
            }

            object[] arguments = { equipment, null };
            bool found = ReflectionCache.TryGetOffHandMethod.Invoke(null, arguments) is bool result && result;
            offHand = arguments[1] as ThingWithComps;
            return found;
        }

        private static class ReflectionCache
        {
            // StaticCacheLifecycle: 仅在 Dual Wield 已启用且资格补丁执行时初始化；不持有游戏对象。
            internal static readonly Type EquipmentExtensionsType =
                AccessTools.TypeByName("DualWield.Ext_Pawn_EquipmentTracker");
            internal static readonly MethodInfo TryGetOffHandMethod = EquipmentExtensionsType == null
                ? null
                : AccessTools.Method(
                    EquipmentExtensionsType,
                    "TryGetOffHandEquipment",
                    new[] { typeof(Pawn_EquipmentTracker), typeof(ThingWithComps).MakeByRefType() });
            internal static readonly MethodInfo AddOffHandEquipmentMethod = EquipmentExtensionsType == null
                ? null
                : AccessTools.Method(
                    EquipmentExtensionsType,
                    "AddOffHandEquipment",
                    new[] { typeof(Pawn_EquipmentTracker), typeof(ThingWithComps) });
            internal static readonly Type ThingExtensionsType = AccessTools.TypeByName("DualWield.Ext_ThingWithComps");
            internal static readonly MethodInfo IsOffHandMethod = ThingExtensionsType == null
                ? null
                : AccessTools.Method(
                    ThingExtensionsType,
                    "IsOffHand",
                    new[] { typeof(ThingWithComps) });
            internal static readonly TryGetOffHandEquipmentDelegate TryGetOffHand = CreateDelegate();

            private static TryGetOffHandEquipmentDelegate CreateDelegate()
            {
                try
                {
                    return TryGetOffHandMethod == null
                        ? null
                        : (TryGetOffHandEquipmentDelegate)TryGetOffHandMethod.CreateDelegate(
                            typeof(TryGetOffHandEquipmentDelegate));
                }
                catch
                {
                    return null;
                }
            }
        }
    }

    internal sealed class DualWieldOffHandTransferState
    {
        private readonly Comp_WeaponWheel wheel;
        private readonly ThingWithComps offHand;
        private bool restored;

        internal DualWieldOffHandTransferState(Comp_WeaponWheel wheel, ThingWithComps offHand)
        {
            this.wheel = wheel;
            this.offHand = offHand;
        }

        internal void Restore()
        {
            if (restored)
            {
                return;
            }
            restored = true;
            DualWieldCompatibility.RestoreOffHand(wheel, offHand);
        }
    }

    [HarmonyPatch(typeof(Comp_WeaponWheel), nameof(Comp_WeaponWheel.IsFullFirepowerEligible))]
    internal static class Harmony_DualWield_WeaponWheelFullFirepower
    {
        public static bool Prepare()
        {
            return DualWieldCompatibility.Active;
        }

        public static void Postfix(Comp_WeaponWheel __instance, ref bool __result)
        {
            if (__result && DualWieldCompatibility.HasOffHand(__instance?.Pawn))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(Comp_WeaponWheel), nameof(Comp_WeaponWheel.HandleBeforeTryStartCast))]
    internal static class Harmony_DualWield_WeaponWheelCastDisplay
    {
        public static bool Prepare()
        {
            return DualWieldCompatibility.Active;
        }

        public static bool Prefix(Comp_WeaponWheel __instance, ref bool result, ref bool __result)
        {
            if (!DualWieldCompatibility.HasOffHand(__instance?.Pawn))
            {
                return true;
            }

            // 双持时由 Dual Wield 完整负责主副手绘制；轮盘不再插入单主手的抬枪动画。
            result = false;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class Harmony_DualWield_WeaponWheelEquipmentNotifications
    {
        public static bool Prepare()
        {
            return DualWieldCompatibility.Active;
        }

        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Comp_WeaponWheel), nameof(Comp_WeaponWheel.NotifyExternalEquipmentAdded));
            yield return AccessTools.Method(typeof(Comp_WeaponWheel), nameof(Comp_WeaponWheel.NotifyExternalEquipmentRemoved));
        }

        public static bool Prefix(ThingWithComps __0)
        {
            // Dual Wield 的副手仍是 EquipmentType.Primary，但它不属于轮盘槽位。
            return !DualWieldCompatibility.IsOffHand(__0);
        }
    }

    [HarmonyPatch]
    internal static class Harmony_DualWield_WeaponWheelPrimarySwap
    {
        public static bool Prepare()
        {
            return DualWieldCompatibility.Active;
        }

        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Comp_WeaponWheel), nameof(Comp_WeaponWheel.MoveActiveWeaponTo));
            yield return AccessTools.Method(typeof(Comp_WeaponWheel), nameof(Comp_WeaponWheel.NormalizeToPrimarySlot));
        }

        public static bool Prefix(
            Comp_WeaponWheel __instance,
            MethodBase __originalMethod,
            object[] __args,
            ref bool __result,
            out DualWieldOffHandTransferState __state)
        {
            __state = null;
            int targetSlot = __originalMethod.Name == nameof(Comp_WeaponWheel.MoveActiveWeaponTo)
                && __args != null && __args.Length > 0 && __args[0] is int slotIndex
                    ? slotIndex
                    : 0;
            if (__instance?.Pawn?.equipment?.Primary == __instance?.WeaponAt(targetSlot))
            {
                return true;
            }

            if (!DualWieldCompatibility.TryDetachOffHand(__instance, out ThingWithComps offHand))
            {
                __result = false;
                return false;
            }
            if (offHand != null)
            {
                __state = new DualWieldOffHandTransferState(__instance, offHand);
            }
            return true;
        }

        public static void Postfix(DualWieldOffHandTransferState __state)
        {
            __state?.Restore();
        }

        public static Exception Finalizer(Exception __exception, DualWieldOffHandTransferState __state)
        {
            __state?.Restore();
            return __exception;
        }
    }
}
