using System;
using System.Reflection;

namespace MooGirl
{
    public static class MeleeAnimationCompat
    {
        // StaticCacheLifecycle: 进程级可选 mod 反射缓存；不持有游戏对象。
        private static readonly FieldInfo AnimateAtIdleField;
        private static readonly FieldInfo SettingsField;

        static MeleeAnimationCompat()
        {
            Type coreType = AccessToolsShim.TypeByName("AM.Core");
            SettingsField = coreType?.GetField("Settings", BindingFlags.Public | BindingFlags.Static);
            Type settingsType = AccessToolsShim.TypeByName("AM.AMSettings.Settings");
            AnimateAtIdleField = settingsType?.GetField("AnimateAtIdle", BindingFlags.Public | BindingFlags.Instance);
        }

        public static IDisposable SuspendIdleWeaponAnimation()
        {
            object settings = SettingsField?.GetValue(null);
            if (settings == null || AnimateAtIdleField == null)
            {
                return NullDisposable.Instance;
            }

            bool originalValue;
            try
            {
                object originalValueObj = AnimateAtIdleField.GetValue(settings);
                if (!(originalValueObj is bool value))
                {
                    return NullDisposable.Instance;
                }

                originalValue = value;
                if (!originalValue)
                {
                    return NullDisposable.Instance;
                }

                AnimateAtIdleField.SetValue(settings, false);
            }
            catch
            {
                return NullDisposable.Instance;
            }

            return new RestoreAnimateAtIdle(settings, originalValue);
        }

        private sealed class RestoreAnimateAtIdle : IDisposable
        {
            private readonly object settings;
            private readonly bool originalValue;
            private bool disposed;

            public RestoreAnimateAtIdle(object settings, bool originalValue)
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
                    AnimateAtIdleField?.SetValue(settings, originalValue);
                }
                catch
                {
                    // 兼容层不能因为对方 mod 内部字段变化而打断渲染流程。
                }
            }
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new NullDisposable();

            public void Dispose()
            {
            }
        }

        private static class AccessToolsShim
        {
            public static Type TypeByName(string name)
            {
                return HarmonyLib.AccessTools.TypeByName(name);
            }
        }
    }
}
