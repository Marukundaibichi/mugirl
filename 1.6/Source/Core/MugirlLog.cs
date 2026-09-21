using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse;

namespace Mugirl
{
    internal static class MugirlLog
    {
        private const string Prefix = "[Mugirl] ";
        private const string UnknownWarningKey = "MugirlLog.WarningOnce.Unknown";
        // StaticCacheLifecycle: 每局游戏的限次警告键；新建、读档和初始化时由 MugirlStoryState 重置。
        private static readonly HashSet<string> warnedKeys = new HashSet<string>();

        internal static void DevMessage(string message)
        {
            if (Prefs.DevMode)
            {
                Log.Message(Prefix + message);
            }
        }

        // 诊断工具输出：开关和前缀由调用方负责（例如武器轮盘开发者日志的设置项），
        // 不依赖 Prefs.DevMode，玩家只需要在 mod 设置里开启对应工具。
        internal static void DiagnosticMessage(string message)
        {
            Log.Message(message);
        }

        private static void Warning(string message)
        {
            Log.Warning(Prefix + message);
        }

        // Mod 构造阶段早于语言初始化；此时不能调用 Translate 或 Verse 的 Formatted。
        // 保留即时诊断，语言就绪后仍使用原翻译键；fallback 应与该键的英文语义一致。
        internal static void StartupWarningOnce(string key, Func<string> localizedMessage, string fallback)
        {
            WarningOnce(key, LanguageDatabase.activeLanguage == null ? fallback : localizedMessage());
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
                return "MugirlLog.WarningOnce.CallSite:" + (callerMemberName ?? string.Empty) + ":" + callerLineNumber;
            }

            return UnknownWarningKey;
        }

        internal static void ResetOnceWarnings()
        {
            warnedKeys.Clear();
        }
    }
}
