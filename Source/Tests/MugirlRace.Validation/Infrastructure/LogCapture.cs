using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MugirlRace.Validation.Infrastructure;

/// <summary>
/// 捕获验证运行 Mod 代码期间写入 <see cref="Log"/> 的全部内容。离线时 Verse.Log 没有输出端，<c>catch</c> 块里的
/// <c>Log.Error</c>（例如 HarmonyBootstrap 报告补丁类失败、Transpiler 找不到锚点）否则会无声消失。
/// </summary>
/// <remarks>
/// 在加载 Mod 程序集、编译其任何方法之前安装：JIT 在补丁存在之前内联的 Log 调用无法再拦截。前缀返回
/// <see langword="false"/>，游戏自己的 Log 代码（消息队列、日志窗口状态）离线时从不运行。
/// </remarks>
internal static class LogCapture
{
    private const string HarmonyId = "mugirl.validation.logcapture";
    private static readonly List<LogEntry> Captured = [];
    private static HashSet<Assembly> attributed = [];

    /// <summary>正在运行的检查 id；随每条记录保存。</summary>
    public static string Phase { get; set; } = "startup";

    public static IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (Captured)
            {
                return Captured.ToList();
            }
        }
    }

    /// <summary>为 <see cref="Log"/> 写消息的六个公开入口打前缀补丁。</summary>
    public static void Install()
    {
        var harmony = new Harmony(HarmonyId);
        Patch(harmony, nameof(Log.Message), [typeof(string)], nameof(CaptureMessage));
        Patch(harmony, nameof(Log.Message), [typeof(object)], nameof(CaptureMessageObject));
        Patch(harmony, nameof(Log.Warning), [typeof(string)], nameof(CaptureWarning));
        Patch(harmony, nameof(Log.WarningOnce), [typeof(string), typeof(int)], nameof(CaptureWarningOnce));
        Patch(harmony, nameof(Log.Error), [typeof(string)], nameof(CaptureError));
        Patch(harmony, nameof(Log.ErrorOnce), [typeof(string), typeof(int)], nameof(CaptureErrorOnce));
    }

    /// <summary>每条记录归属到调用栈中第一个属于这些程序集的帧。</summary>
    public static void AttributeTo(IEnumerable<Assembly> assemblies) => attributed = [.. assemblies];

    private static void Patch(Harmony harmony, string method, Type[] parameters, string prefix)
    {
        MethodInfo original = AccessTools.DeclaredMethod(typeof(Log), method, parameters)
            ?? throw new EnvironmentException($"Verse.Log.{method}({string.Join(", ", parameters.Select(p => p.Name))}) does not exist in this game build.");
        harmony.Patch(original, prefix: new HarmonyMethod(typeof(LogCapture), prefix) { priority = Priority.First });
    }

    private static bool CaptureMessage(string text) => Add(LogLevel.Message, text);

    private static bool CaptureMessageObject(object obj) => Add(LogLevel.Message, obj?.ToString() ?? "null");

    private static bool CaptureWarning(string text) => Add(LogLevel.Warning, text);

    private static bool CaptureWarningOnce(string text) => Add(LogLevel.Warning, text);

    private static bool CaptureError(string text) => Add(LogLevel.Error, text);

    private static bool CaptureErrorOnce(string text) => Add(LogLevel.Error, text);

    private static bool Add(LogLevel level, string? text)
    {
        string origin = "(game)";
        Type? originType = null;
        foreach (StackFrame frame in new StackTrace(skipFrames: 2).GetFrames())
        {
            MethodBase? method = frame.GetMethod();
            if (method?.DeclaringType is { } type && attributed.Contains(type.Assembly))
            {
                origin = type.FullName + "." + method.Name;
                originType = type;
                break;
            }
        }

        lock (Captured)
        {
            Captured.Add(new LogEntry(level, text ?? "", origin, Phase, originType));
        }

        return false;
    }
}
