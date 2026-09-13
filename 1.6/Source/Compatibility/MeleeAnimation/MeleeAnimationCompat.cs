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

        internal static IdleAnimationState SuspendIdleWeaponAnimation()
        {
            if (ReflectionCache.SettingsAccessor == null || ReflectionCache.AnimateAtIdleAccessor == null)
            {
                return default;
            }

            try
            {
                // 读取当前 Settings 实例，允许对方运行期替换设置；只缓存字段访问器。
                object settings = ReflectionCache.SettingsAccessor();
                if (settings == null || !ReflectionCache.AnimateAtIdleAccessor(settings))
                {
                    return default;
                }

                ReflectionCache.AnimateAtIdleAccessor(settings) = false;
                return new IdleAnimationState(settings);
            }
            catch
            {
                return default;
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
            internal static readonly AccessTools.FieldRef<object> SettingsAccessor = CreateSettingsAccessor();
            internal static readonly AccessTools.FieldRef<object, bool> AnimateAtIdleAccessor = CreateAnimateAtIdleAccessor();

            private static AccessTools.FieldRef<object> CreateSettingsAccessor()
            {
                try
                {
                    return SettingsField != null && !SettingsField.FieldType.IsValueType
                        ? AccessTools.StaticFieldRefAccess<object>(SettingsField)
                        : null;
                }
                catch
                {
                    return null;
                }
            }

            private static AccessTools.FieldRef<object, bool> CreateAnimateAtIdleAccessor()
            {
                try
                {
                    return AnimateAtIdleField?.FieldType == typeof(bool)
                        ? AccessTools.FieldRefAccess<object, bool>(AnimateAtIdleField)
                        : null;
                }
                catch
                {
                    return null;
                }
            }
        }

        internal struct IdleAnimationState
        {
            private object settings;

            internal IdleAnimationState(object settings)
            {
                this.settings = settings;
            }

            internal void Restore()
            {
                object originalSettings = settings;
                if (originalSettings == null)
                {
                    return;
                }

                // Postfix 与 Finalizer 共享 ref __state；先清标记，确保只恢复一次。
                // 嵌套调用看到 false 时返回默认状态，由最外层恢复原来的 true。
                settings = null;
                try
                {
                    ReflectionCache.AnimateAtIdleAccessor(originalSettings) = true;
                }
                catch
                {
                    // 兼容层不能因为对方 mod 内部字段变化而打断渲染流程。
                }
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
        public static void Prefix(Pawn ___pawn, ref MeleeAnimationCompat.IdleAnimationState __state)
        {
            if (WeaponWheelHarmonyUtility.CompFor(___pawn)?.ShouldSuppressExternalMeleeAttackAnimation() == true)
            {
                __state = MeleeAnimationCompat.SuspendIdleWeaponAnimation();
            }
        }

        public static void Postfix(ref MeleeAnimationCompat.IdleAnimationState __state)
        {
            __state.Restore();
        }

        public static Exception Finalizer(Exception __exception, ref MeleeAnimationCompat.IdleAnimationState __state)
        {
            __state.Restore();
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

        public static void Prefix(Comp_MugirlMount comp, ref MeleeAnimationCompat.IdleAnimationState __state)
        {
            ThingWithComps weapon = comp?.MountedPawn?.equipment?.Primary;
            if (weapon?.def?.IsMeleeWeapon == true)
            {
                __state = MeleeAnimationCompat.SuspendIdleWeaponAnimation();
            }
        }

        public static void Postfix(ref MeleeAnimationCompat.IdleAnimationState __state)
        {
            __state.Restore();
        }

        public static Exception Finalizer(Exception __exception, ref MeleeAnimationCompat.IdleAnimationState __state)
        {
            __state.Restore();
            return __exception;
        }
    }
}
