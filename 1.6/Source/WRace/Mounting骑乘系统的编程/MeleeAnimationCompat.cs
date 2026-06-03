using System;
using System.Reflection;

namespace MooGirl
{
    public static class MeleeAnimationCompat
    {
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
                originalValue = (bool)AnimateAtIdleField.GetValue(settings);
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
                    // Compatibility should never break rendering if the other mod changes internals.
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
