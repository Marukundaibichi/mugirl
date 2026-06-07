using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse;

namespace MooGirl
{
    internal static class MooGirlLog
    {
        private const string Prefix = "[MooGirl] ";
        private const string UnknownWarningKey = "MooGirlLog.WarningOnce.Unknown";
        // StaticCacheLifecycle: 每局游戏的限次警告键；新建、读档和初始化时由 MooGirlStoryState 重置。
        private static readonly HashSet<string> warnedKeys = new HashSet<string>();

        internal static void DevMessage(string message)
        {
            if (Prefs.DevMode)
            {
                Log.Message(Prefix + message);
            }
        }

        private static void Warning(string message)
        {
            Log.Warning(Prefix + message);
        }

        internal static void WarningOnce(
            string key,
            string message,
            [CallerMemberName] string callerMemberName = null,
            [CallerLineNumber] int callerLineNumber = 0)
        {
            string warningKey = string.IsNullOrWhiteSpace(key)
                ? BuildFallbackKey(callerMemberName, callerLineNumber)
                : key.Trim();
            if (warnedKeys.Add(warningKey))
            {
                Warning(string.IsNullOrWhiteSpace(message) ? "Unspecified warning." : message);
            }
        }

        private static string BuildFallbackKey(string callerMemberName, int callerLineNumber)
        {
            if (!string.IsNullOrEmpty(callerMemberName) || callerLineNumber > 0)
            {
                return "MooGirlLog.WarningOnce.CallSite:" + (callerMemberName ?? string.Empty) + ":" + callerLineNumber;
            }

            return UnknownWarningKey;
        }

        internal static void ResetOnceWarnings()
        {
            warnedKeys.Clear();
        }
    }
}
