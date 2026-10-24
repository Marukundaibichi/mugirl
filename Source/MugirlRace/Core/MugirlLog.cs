using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Mugirl.Core;

/// <summary>
/// 本 Mod 唯一的日志入口；业务代码不直接调用 <see cref="Verse.Log"/>（BannedSymbols.txt 禁用）。
/// </summary>
/// <remarks>
/// 1.7 的 DrawPhase.ParallelPreDraw 在工作线程上运行 Render Node 与部分绘制补丁，这里的所有成员都可以从任意线程调用。
/// 带 <see cref="Func{TResult}"/> 的重载只在消息确实要输出时才求值，用于翻译或拼接开销较大的重复路径。
/// </remarks>
internal static class MugirlLog
{
    internal const string Prefix = "[Mugirl] ";
    private const string UnknownWarningKey = "MugirlLog.WarningOnce.Unknown";
    private const string ErrorKeyPrefix = "error:";

    // StaticCacheLifecycle: 每局限次日志键；在原瞬态重置点由生命周期中枢清空。
    // 并行绘制阶段可能并发写入，所以用 ConcurrentDictionary 代替 HashSet。
    private static readonly ConcurrentDictionary<string, byte> onceKeys = new(StringComparer.Ordinal);

    internal static void Message(string message) => WriteMessage(Prefix + message);

    internal static void DevMessage(string message)
    {
        if (Prefs.DevMode)
        {
            WriteMessage(Prefix + message);
        }
    }

    internal static void DevMessage(Func<string> message)
    {
        if (Prefs.DevMode)
        {
            WriteMessage(Prefix + Evaluate(message));
        }
    }

    // 诊断工具输出：开关和前缀由调用方负责（例如武器轮盘开发者日志的设置项），
    // 不依赖 Prefs.DevMode，玩家只需要在 mod 设置里开启对应工具。
    internal static void DiagnosticMessage(string message) => WriteMessage(message);

    internal static void Warning(string message) => WriteWarning(Prefix + message);

    internal static void Warning(Func<string> message) => WriteWarning(Prefix + Evaluate(message));

    internal static void WarningOnce(
        string? key,
        string? message,
        [CallerMemberName] string? callerMemberName = null,
        [CallerLineNumber] int callerLineNumber = 0)
    {
        if (TryMarkOnce(NormalizeWarningKey(key, callerMemberName, callerLineNumber)))
        {
            Warning(string.IsNullOrWhiteSpace(message) ? "Unspecified warning." : message);
        }
    }

    internal static void WarningOnce(
        string? key,
        Func<string> message,
        [CallerMemberName] string? callerMemberName = null,
        [CallerLineNumber] int callerLineNumber = 0)
    {
        if (TryMarkOnce(NormalizeWarningKey(key, callerMemberName, callerLineNumber)))
        {
            string text = Evaluate(message);
            Warning(string.IsNullOrWhiteSpace(text) ? "Unspecified warning." : text);
        }
    }

    // Mod 构造阶段早于语言初始化；此时不能调用 Translate 或 Verse 的 Formatted。
    // 语言就绪前输出英文 fallback，就绪后才求值翻译回调；fallback 应与该键的英文语义一致。
    internal static void StartupWarningOnce(string key, Func<string> localizedMessage, string fallback)
    {
        string warningKey = NormalizeWarningKey(key, null, 0);
        if (TryMarkOnce(warningKey))
        {
            Warning(LanguageDatabase.activeLanguage is null ? fallback : Evaluate(localizedMessage));
        }
    }

    internal static void Error(string message) => WriteError(Prefix + message);

    internal static void Error(Func<string> message) => WriteError(Prefix + Evaluate(message));

    internal static void ErrorOnce(string key, string message)
    {
        if (TryMarkOnce(ErrorKeyPrefix + key))
        {
            Error(message);
        }
    }

    internal static void ErrorOnce(string key, Func<string> message)
    {
        if (TryMarkOnce(ErrorKeyPrefix + key))
        {
            Error(Evaluate(message));
        }
    }

    /// <summary>记录一条带完整异常（含内部异常与堆栈）的错误。</summary>
    internal static void Exception(string context, Exception exception) => Error(context + "\n" + exception);

    /// <summary>同 <see cref="Exception(string, System.Exception)"/>，但每个 <paramref name="key"/> 每局只记录一次。</summary>
    internal static void ExceptionOnce(string key, string context, Exception exception)
    {
        if (TryMarkOnce(ErrorKeyPrefix + key))
        {
            Exception(context, exception);
        }
    }

    internal static void ResetOnceWarnings() => onceKeys.Clear();

    /// <summary>
    /// 稳定的 FNV-1a 字符串哈希，用于把长文本压缩成限次日志键。
    /// </summary>
    /// <remarks>
    /// <see cref="string.GetHashCode()"/> 在游戏运行时（WaylithClr.MonoStringHashCode）与标准 .NET（每进程随机）上结果不同，
    /// 离线验证与游戏内会得到不同的键；这里的结果在任何运行时上都相同。
    /// </remarks>
    internal static int StableHash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in value)
            {
                hash = (hash ^ c) * 16777619;
            }

            return (int)hash;
        }
    }

    private static bool TryMarkOnce(string key) => onceKeys.TryAdd(key, 0);

    private static string NormalizeWarningKey(string? key, string? callerMemberName, int callerLineNumber)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key.Trim();
        }

        if (!string.IsNullOrEmpty(callerMemberName) || callerLineNumber > 0)
        {
            return "MugirlLog.WarningOnce.CallSite:" + (callerMemberName ?? string.Empty) + ":" + callerLineNumber;
        }

        return UnknownWarningKey;
    }

    // 日志调用不能因为消息构造失败而向业务路径抛出异常（例如翻译键参数错误）。
    private static string Evaluate(Func<string> message)
    {
        try
        {
            return message();
        }
        catch (Exception ex)
        {
            return "(log message could not be built: " + ex.GetType().Name + ": " + ex.Message + ")";
        }
    }

#pragma warning disable RS0030 // MugirlLog 是唯一允许直接调用 Verse.Log 的位置；其余代码经由本类输出。
    private static void WriteMessage(string text) => Log.Message(text);

    private static void WriteWarning(string text) => Log.Warning(text);

    private static void WriteError(string text) => Log.Error(text);
#pragma warning restore RS0030
}
