using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mugirl.Features.WeaponWheel;
using RimWorld;
using Verse;

namespace Mugirl
{
    internal static class MeleeAnimationCompat
    {
        private const string PackageId = "co.uk.epicguru.meleeanimation";

        internal static bool Active => ModLister.GetActiveModWithIdentifier(PackageId) != null;

        internal static IDisposable SuspendIdleWeaponAnimation()
        {
            object settings = ReflectionCache.SettingsField?.GetValue(null);
            if (settings == null || ReflectionCache.AnimateAtIdleField == null)
            {
                return NullDisposable.Instance;
            }

            try
            {
                object originalValueObj = ReflectionCache.AnimateAtIdleField.GetValue(settings);
                if (!(originalValueObj is bool originalValue) || !originalValue)
                {
                    return NullDisposable.Instance;
                }

                ReflectionCache.AnimateAtIdleField.SetValue(settings, false);
                return new RestoreAnimateAtIdle(settings, originalValue);
            }
            catch
            {
                return NullDisposable.Instance;
            }
        }

        internal static void NotifyPrimaryWeaponChanged(Pawn pawn)
        {
            if (pawn?.AllComps == null || ReflectionCache.IdleControllerCompType == null
                || ReflectionCache.ClearAnimationMethod == null)
            {
                return;
            }

            for (int i = 0; i < pawn.AllComps.Count; i++)
            {
                ThingComp comp = pawn.AllComps[i];
                if (comp == null || !ReflectionCache.IdleControllerCompType.IsInstanceOfType(comp))
                {
                    continue;
                }

                try
                {
                    ReflectionCache.ClearAnimationMethod.Invoke(comp, null);
                }
                catch
                {
                    // 可选兼容层在对方内部实现变化时静默降级。
                }
                return;
            }
        }

        private static class ReflectionCache
        {
            // StaticCacheLifecycle: 仅在 Melee Animation 已启用且兼容补丁实际执行时初始化；不持有游戏对象。
            internal static readonly Type IdleControllerCompType = AccessTools.TypeByName("AM.Idle.IdleControllerComp");
            internal static readonly MethodInfo ClearAnimationMethod = IdleControllerCompType?.GetMethod(
                "ClearAnimation",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            internal static readonly FieldInfo SettingsField = AccessTools.TypeByName("AM.Core")
                ?.GetField("Settings", BindingFlags.Public | BindingFlags.Static);
            internal static readonly FieldInfo AnimateAtIdleField = AccessTools.TypeByName("AM.AMSettings.Settings")
                ?.GetField("AnimateAtIdle", BindingFlags.Public | BindingFlags.Instance);
        }

        private sealed class RestoreAnimateAtIdle : IDisposable
        {
            private readonly object settings;
            private readonly bool originalValue;
            private bool disposed;

            internal RestoreAnimateAtIdle(object settings, bool originalValue)
            {
                this.settings = settings;
                this.originalValue = originalValue;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                try
                {
                    ReflectionCache.AnimateAtIdleField?.SetValue(settings, originalValue);
                }
                catch
                {
                    // 兼容层不能因为对方 mod 内部字段变化而打断渲染流程。
                }
            }
        }

        private sealed class NullDisposable : IDisposable
        {
            internal static readonly NullDisposable Instance = new NullDisposable();

            public void Dispose()
            {
            }
        }
    }

    [HarmonyPatch]
    internal static class Harmony_MeleeAnimation_WeaponWheelPrimaryChanged
    {
        public static bool Prepare()
        {
            return MeleeAnimationCompat.Active;
        }

        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Comp_WeaponWheel), nameof(Comp_WeaponWheel.MoveActiveWeaponTo));
            yield return AccessTools.Method(typeof(Comp_WeaponWheel), nameof(Comp_WeaponWheel.NormalizeToPrimarySlot));
        }

        public static void Prefix(Comp_WeaponWheel __instance, out ThingWithComps __state)
        {
            __state = __instance?.Pawn?.equipment?.Primary;
        }

        public static void Postfix(Comp_WeaponWheel __instance, ThingWithComps __state, bool __result)
        {
            Pawn pawn = __instance?.Pawn;
            if (__result && pawn?.equipment?.Primary != __state)
            {
                MeleeAnimationCompat.NotifyPrimaryWeaponChanged(pawn);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.Notify_MeleeAttackOn))]
    internal static class Harmony_WeaponWheel_MeleeAnimationScope
    {
        public static bool Prepare()
        {
            return MeleeAnimationCompat.Active;
        }

        [HarmonyPriority(Priority.First)]
        public static void Prefix(Pawn ___pawn, ref IDisposable __state)
        {
            if (___pawn?.TryGetComp<Comp_WeaponWheel>()?.ShouldSuppressExternalMeleeAttackAnimation() == true)
            {
                __state = MeleeAnimationCompat.SuspendIdleWeaponAnimation();
            }
        }

        public static void Postfix(IDisposable __state)
        {
            __state?.Dispose();
        }

        public static Exception Finalizer(Exception __exception, IDisposable __state)
        {
            __state?.Dispose();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(MountedPawnMeleeSupport), nameof(MountedPawnMeleeSupport.DrawWeapon))]
    internal static class Harmony_MeleeAnimation_MountedWeaponDraw
    {
        public static bool Prepare()
        {
            return MeleeAnimationCompat.Active;
        }

        public static void Prefix(Comp_MugirlMount comp, ref IDisposable __state)
        {
            ThingWithComps weapon = comp?.MountedPawn?.equipment?.Primary;
            if (weapon?.def?.IsMeleeWeapon == true)
            {
                __state = MeleeAnimationCompat.SuspendIdleWeaponAnimation();
            }
        }

        public static void Postfix(IDisposable __state)
        {
            __state?.Dispose();
        }

        public static Exception Finalizer(Exception __exception, IDisposable __state)
        {
            __state?.Dispose();
            return __exception;
        }
    }
}
