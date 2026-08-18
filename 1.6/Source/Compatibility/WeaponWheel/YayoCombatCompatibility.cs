using System;
using System.Reflection;
using HarmonyLib;
using Mugirl.Features.WeaponWheel;
using RimWorld;
using RimWorld.Utility;
using UnityEngine;
using Verse;

namespace Mugirl
{
    internal static class YayoCombatCompatibility
    {
        private const string PackageId = "Mlie.YayosCombat3";

        private static bool reloadInvocationFailed;
        private static bool generatedAmmoInitializationFailed;

        internal static bool Active => ModLister.GetActiveModWithIdentifier(PackageId) != null;

        internal static bool AmmoEnabled => ReflectionCache.TryAutoReloadMethod != null
            && ReflectionCache.AmmoEnabledField?.GetValue(null) is bool enabled
            && enabled;

        internal static void TryAutoReloadReserveWeapon(Comp_WeaponWheel wheel)
        {
            Pawn pawn = wheel?.Pawn;
            if (pawn == null || wheel.IsBusy || reloadInvocationFailed || !AmmoEnabled
                || !pawn.Drafted || !Gen.IsHashIntervalTick(pawn, 60)
                || (pawn.CurJobDef != JobDefOf.Wait_Combat && pawn.CurJobDef != JobDefOf.AttackStatic))
            {
                return;
            }

            CompApparelReloadable primaryReloadable = pawn.equipment?.Primary?.TryGetComp<CompApparelReloadable>();
            if (primaryReloadable?.NeedsReload(false) == true)
            {
                return;
            }

            ThingOwner<ThingWithComps> reserveWeapons = wheel.ReserveWeapons;
            for (int i = 0; i < reserveWeapons.Count; i++)
            {
                CompApparelReloadable reloadable = reserveWeapons[i]?.TryGetComp<CompApparelReloadable>();
                if (reloadable?.NeedsReload(false) != true)
                {
                    continue;
                }

                try
                {
                    ReflectionCache.TryAutoReloadMethod.Invoke(null, new object[] { reloadable });
                }
                catch (Exception ex)
                {
                    reloadInvocationFailed = true;
                    MugirlLog.WarningOnce(
                        "WeaponWheel.YayoCombatAutoReloadFailed",
                        "Weapon wheel could not invoke Yayo's Combat reserve-weapon reload support: "
                        + ex.GetType().Name + ": " + ex.Message);
                }
                return;
            }
        }

        internal static void InitializeGeneratedWeaponAmmo(Pawn pawn, ThingWithComps weapon)
        {
            if (pawn == null || weapon == null || generatedAmmoInitializationFailed || !AmmoEnabled
                || ReflectionCache.EnemyAmmoFactorField == null || ReflectionCache.RemainingChargesField == null)
            {
                return;
            }

            CompApparelReloadable reloadable = weapon.TryGetComp<CompApparelReloadable>();
            if (reloadable == null || !(ReflectionCache.EnemyAmmoFactorField.GetValue(null) is float ammoFactor))
            {
                return;
            }

            try
            {
                int charges = Mathf.RoundToInt(reloadable.MaxCharges * ammoFactor * Rand.Range(0.7f, 1.3f));
                if (ammoFactor <= 1f || pawn.Faction?.IsPlayer == true)
                {
                    charges = Mathf.Min(reloadable.MaxCharges, charges);
                }
                ReflectionCache.RemainingChargesField.SetValue(reloadable, Mathf.Max(0, charges));
            }
            catch (Exception ex)
            {
                generatedAmmoInitializationFailed = true;
                MugirlLog.WarningOnce(
                    "WeaponWheel.YayoCombatGeneratedAmmoFailed",
                    "Weapon wheel could not initialize Yayo's Combat ammo for a generated reserve weapon: "
                    + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static class ReflectionCache
        {
            // StaticCacheLifecycle: 仅在 Yayo's Combat 已启用且兼容补丁执行时初始化；不持有游戏对象。
            internal static readonly Type CoreType = AccessTools.TypeByName("yayoCombat.YayoCombatCore");
            internal static readonly Type ReloadUtilityType = AccessTools.TypeByName("yayoCombat.reloadUtility");
            internal static readonly FieldInfo AmmoEnabledField = CoreType == null
                ? null
                : AccessTools.Field(CoreType, "ammo");
            internal static readonly FieldInfo EnemyAmmoFactorField = CoreType == null
                ? null
                : AccessTools.Field(CoreType, "s_enemyAmmo");
            internal static readonly FieldInfo RemainingChargesField = AccessTools.Field(
                typeof(CompApparelVerbOwner_Charged),
                "remainingCharges");
            internal static readonly MethodInfo TryAutoReloadMethod = ReloadUtilityType == null
                ? null
                : AccessTools.Method(
                    ReloadUtilityType,
                    "TryAutoReload",
                    new[] { typeof(CompApparelReloadable) });
        }
    }

    [HarmonyPatch(typeof(Comp_WeaponWheel), nameof(Comp_WeaponWheel.CompTick))]
    internal static class Harmony_YayoCombat_WeaponWheelTick
    {
        public static bool Prepare()
        {
            return YayoCombatCompatibility.Active;
        }

        public static void Postfix(Comp_WeaponWheel __instance)
        {
            YayoCombatCompatibility.TryAutoReloadReserveWeapon(__instance);
        }
    }

    [HarmonyPatch(typeof(Comp_WeaponWheel), nameof(Comp_WeaponWheel.TryAddGeneratedReserveWeapon))]
    internal static class Harmony_YayoCombat_GeneratedWeaponAmmo
    {
        public static bool Prepare()
        {
            return YayoCombatCompatibility.Active;
        }

        public static void Prefix(Comp_WeaponWheel __instance, ThingWithComps weapon)
        {
            YayoCombatCompatibility.InitializeGeneratedWeaponAmmo(__instance?.Pawn, weapon);
        }
    }

    [HarmonyPatch(typeof(CompApparelVerbOwner), nameof(CompApparelVerbOwner.Wearer), MethodType.Getter)]
    internal static class Harmony_WeaponWheel_ReloadableWearer
    {
        public static bool Prepare()
        {
            return YayoCombatCompatibility.Active;
        }

        public static void Postfix(CompApparelVerbOwner __instance, ref Pawn __result)
        {
            if (__result == null && __instance?.parent?.ParentHolder is Comp_WeaponWheel wheel)
            {
                __result = wheel.Pawn;
            }
        }
    }

    [HarmonyPatch(typeof(ReloadableUtility), nameof(ReloadableUtility.OwnerOf))]
    internal static class Harmony_WeaponWheel_ReloadableOwner
    {
        public static bool Prepare()
        {
            return YayoCombatCompatibility.Active;
        }

        [HarmonyAfter("Mlie.YayosCombat3")]
        public static void Postfix(IReloadableComp reloadable, ref Pawn __result)
        {
            if (__result == null && reloadable?.ReloadableThing?.ParentHolder is Comp_WeaponWheel wheel)
            {
                __result = wheel.Pawn;
            }
        }
    }

    [HarmonyPatch(typeof(ReloadableUtility), nameof(ReloadableUtility.FindSomeReloadableComponent))]
    internal static class Harmony_WeaponWheel_FindReloadable
    {
        public static bool Prepare()
        {
            return YayoCombatCompatibility.Active;
        }

        [HarmonyAfter("Mlie.YayosCombat3")]
        public static void Postfix(Pawn pawn, bool allowForcedReload, ref IReloadableComp __result)
        {
            if (__result != null || !YayoCombatCompatibility.AmmoEnabled)
            {
                return;
            }

            ThingOwner<ThingWithComps> reserveWeapons = WeaponWheelHarmonyUtility.CompFor(pawn)?.ReserveWeapons;
            if (reserveWeapons == null)
            {
                return;
            }

            for (int i = 0; i < reserveWeapons.Count; i++)
            {
                ThingWithComps weapon = reserveWeapons[i];
                for (int j = 0; j < weapon.AllComps.Count; j++)
                {
                    if (weapon.AllComps[j] is IReloadableComp reloadable && reloadable.NeedsReload(allowForcedReload))
                    {
                        __result = reloadable;
                        return;
                    }
                }
            }
        }
    }
}
