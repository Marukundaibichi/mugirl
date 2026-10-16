using System.Reflection;
using HarmonyLib;
using Mugirl.Features.Mounting;
using Mugirl.Features.WeaponWheel;

namespace Mugirl.Compatibility.MeleeAnimation;

/// <summary>
/// Melee Animation（co.uk.epicguru.meleeanimation）：临时关闭空闲武器动画，以及主武器切换后清除缓存的空闲动画。
/// </summary>
/// <remarks>成员按 1.6 版 zAnimationMod.dll 核对：<c>AM.Core.Settings</c>、<c>AM.AMSettings.Settings.AnimateAtIdle</c>、<c>AM.Idle.IdleControllerComp.ClearAnimation()</c>。</remarks>
internal static class MeleeAnimationCompatibility
{
    internal static bool Active => ModCompat.MeleeAnimation;

    internal static void RegisterHooks()
    {
        if (Active)
        {
            WeaponWheelHooks.Current.AddPrimaryChangedListener("MeleeAnimation", NotifyPrimaryWeaponChanged);
            MountingHooks.RegisterMeleeDraw("MeleeAnimation", BeginMountedDraw, RestoreMountedDraw);
        }
    }

    private static object? BeginMountedDraw(CompMount? comp)
    {
        ThingWithComps? weapon = comp?.MountedPawn?.equipment?.Primary;
        return weapon?.def?.IsMeleeWeapon == true ? SuspendIdleWeaponAnimation().Token : null;
    }

    private static void RestoreMountedDraw(object token) => new IdleAnimationState(token).Restore();

    internal static IdleAnimationState SuspendIdleWeaponAnimation()
    {
        if (ReflectionCache.SettingsAccessor is not { } settingsAccessor
            || ReflectionCache.AnimateAtIdleAccessor is not { } animateAtIdle)
        {
            return default;
        }

        try
        {
            // 每次读取当前 Settings 实例，允许对方运行期替换设置对象；只缓存字段访问器。
            object? settings = settingsAccessor();
            if (settings is null || !animateAtIdle(settings))
            {
                return default;
            }

            animateAtIdle(settings) = false;
            return new IdleAnimationState(settings);
        }
        catch (Exception exception)
        {
            WarnOnce("SuspendIdle", exception);
            return default;
        }
    }

    internal static void NotifyPrimaryWeaponChanged(Pawn? pawn)
    {
        List<ThingComp>? comps = pawn?.AllComps;
        if (comps is null || ReflectionCache.IdleControllerCompType is not { } controllerType
            || ReflectionCache.ClearAnimation is not { } clearAnimation)
        {
            return;
        }

        for (int i = 0; i < comps.Count; i++)
        {
            ThingComp comp = comps[i];
            if (comp is null || !controllerType.IsInstanceOfType(comp))
            {
                continue;
            }

            try
            {
                clearAnimation(comp);
            }
            catch (Exception exception)
            {
                // 对方内部实现变化时只降级为不清除缓存动画，不能打断轮盘切换。
                WarnOnce("ClearAnimation", exception);
            }

            return;
        }
    }

    private static void WarnOnce(string operation, Exception exception)
    {
        MugirlLog.WarningOnce(
            "Compatibility.MeleeAnimation." + operation,
            () => "Melee Animation compatibility (" + operation + ") failed and was skipped: "
                + exception.GetType().Name + ": " + exception.Message);
    }

    private static class ReflectionCache
    {
        // StaticCacheLifecycle: 进程级；兼容补丁第一次执行时解析（此时对方程序集早已加载），只缓存类型与访问器，不持有游戏对象，无需清理。
        internal static readonly Type? IdleControllerCompType;
        internal static readonly Action<ThingComp>? ClearAnimation;
        internal static readonly AccessTools.FieldRef<object>? SettingsAccessor;
        internal static readonly AccessTools.FieldRef<object, bool>? AnimateAtIdleAccessor;

        static ReflectionCache()
        {
            IdleControllerCompType = AccessTools.TypeByName("AM.Idle.IdleControllerComp");
            ClearAnimation = CompatAccessors.InstanceAction<ThingComp>(
                IdleControllerCompType?.GetMethod("ClearAnimation", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null));
            SettingsAccessor = CreateSettingsAccessor(
                AccessTools.TypeByName("AM.Core")?.GetField("Settings", BindingFlags.Public | BindingFlags.Static));
            AnimateAtIdleAccessor = CreateAnimateAtIdleAccessor(
                AccessTools.TypeByName("AM.AMSettings.Settings")?.GetField("AnimateAtIdle", BindingFlags.Public | BindingFlags.Instance));

            if (ClearAnimation is null)
            {
                CompatAccessors.ReportUnbound("Melee Animation", "AM.Idle.IdleControllerComp.ClearAnimation");
            }

            if (SettingsAccessor is null)
            {
                CompatAccessors.ReportUnbound("Melee Animation", "AM.Core.Settings");
            }

            if (AnimateAtIdleAccessor is null)
            {
                CompatAccessors.ReportUnbound("Melee Animation", "AM.AMSettings.Settings.AnimateAtIdle");
            }
        }

        private static AccessTools.FieldRef<object>? CreateSettingsAccessor(FieldInfo? field)
        {
            try
            {
                return field is not null && !field.FieldType.IsValueType
                    ? AccessTools.StaticFieldRefAccess<object>(field)
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static AccessTools.FieldRef<object, bool>? CreateAnimateAtIdleAccessor(FieldInfo? field)
        {
            try
            {
                return field?.FieldType == typeof(bool)
                    ? AccessTools.FieldRefAccess<object, bool>(field)
                    : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>被临时关闭的 AnimateAtIdle；仅在单次绘制 scope 内携带设置对象。</summary>
    internal struct IdleAnimationState
    {
        private object? settings;

        internal readonly object? Token => settings;

        internal IdleAnimationState(object settings)
        {
            this.settings = settings;
        }

        internal void Restore()
        {
            object? originalSettings = settings;
            if (originalSettings is null)
            {
                return;
            }

            // 先清标记，确保只恢复一次。
            // 嵌套调用看到 false 时返回默认状态，由最外层恢复原来的 true。
            settings = null;
            try
            {
                if (ReflectionCache.AnimateAtIdleAccessor is { } animateAtIdle)
                {
                    animateAtIdle(originalSettings) = true;
                }
            }
            catch (Exception exception)
            {
                // 兼容层不能因为对方内部字段变化而打断渲染或战斗流程。
                WarnOnce("RestoreIdle", exception);
            }
        }
    }
}
