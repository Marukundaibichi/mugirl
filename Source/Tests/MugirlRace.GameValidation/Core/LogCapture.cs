using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Waylith.Logging;

namespace Mugirl.GameValidation.Core;

/// <summary>捕获到的一条日志：Verse 日志（类别 "Verse"）与引擎日志都经过 Waylith 的 LogStream。</summary>
internal sealed record CapturedLog(LogLevel Level, string Category, string Message, string? StackTrace, string Scope, bool DuringStep)
{
    public bool MentionsMod => LogCapture.MentionsMod(Message) || (StackTrace is not null && LogCapture.MentionsMod(StackTrace));

    public bool IsWarningOrWorse => Level >= LogLevel.Warning;

    public bool IsErrorOrWorse => Level >= LogLevel.Error;

    /// <summary>一行摘要：级别、类别、首行消息，以及首个指向被测 Mod 的消息或调用栈行。</summary>
    public string Describe()
    {
        string first = FirstLine(Message);
        string where = LogCapture.FirstModLine(Message) ?? (StackTrace is null ? null : LogCapture.FirstModLine(StackTrace)) ?? string.Empty;
        if (where.Length > 0 && !string.Equals(where, first, StringComparison.Ordinal))
        {
            where = " <- " + where;
        }

        return $"{Level} [{Category}] {first}{where} (during {Scope})";
    }

    private static string FirstLine(string text)
    {
        int end = text.IndexOfAny(['\r', '\n']);
        string line = (end < 0 ? text : text[..end]).Trim();
        return line.Length > 400 ? line[..400] + "…" : line;
    }
}

/// <summary>订阅 Waylith.Logging.LogStream，收集警告、错误和提到被测 Mod 的消息。</summary>
/// <remarks>
/// LogStream 的回调可能来自渲染线程、Shader 编译线程或线程池，所以回调里只入队，由会话在主线程取出。
/// 订阅发生在测试模组的 Mod 构造函数中（<see cref="GameValidationMod"/>，早于加载 Def 与贴图）；在它之前的 Verse 日志
/// （例如排在前面的 Mod 的构造函数）由会话从 Verse 的 Log.Messages 快照补齐，更早的引擎日志无法捕获。
/// 有订阅者时 Waylith 为每条日志生成调用栈，这只影响测试进程的性能。
/// </remarks>
internal static partial class LogCapture
{
    // StaticCacheLifecycle: 进程级；整次运行一个队列，会话每帧取空，运行结束时退订。
    private static readonly ConcurrentQueue<CapturedLog> Pending = new();
    private static volatile string scope = "load";
    private static volatile bool duringStep;
    private static bool installed;

    public static void Install()
    {
        if (!installed)
        {
            LogStream.MessageReceived += OnMessage;
            installed = true;
        }
    }

    public static void Uninstall()
    {
        if (installed)
        {
            LogStream.MessageReceived -= OnMessage;
            installed = false;
        }
    }

    public static void SetScope(string name, bool isStep)
    {
        scope = name;
        duringStep = isStep;
    }

    public static void DrainInto(List<CapturedLog> target)
    {
        while (Pending.TryDequeue(out CapturedLog? entry))
        {
            target.Add(entry);
        }
    }

    /// <summary>
    /// 是否提到被测 Mod。测试模组自己的名字（程序集、packageId、Harmony ID、临时目录）也含 Mugirl，先去掉，
    /// 否则测试代码调用原版方法时记录的调用栈都会被算到被测 Mod 头上。
    /// </summary>
    public static bool MentionsMod(string text)
        => text.Contains("mugirl", StringComparison.OrdinalIgnoreCase)
            && OwnNames().Replace(text, string.Empty).Contains("mugirl", StringComparison.OrdinalIgnoreCase);

    public static string? FirstModLine(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            if (MentionsMod(line))
            {
                string trimmed = line.Trim();
                return trimmed.Length > 300 ? trimmed[..300] + "…" : trimmed;
            }
        }

        return null;
    }

    /// <summary>宿主固定输出、与模组无关的行（E48 的底噪列表）以及消息为空的警告。</summary>
    public static bool IsHostNoise(CapturedLog entry)
        => string.IsNullOrWhiteSpace(entry.Message) || HostNoise().IsMatch(entry.Message);

    private static void OnMessage(LogLevel level, string category, string message, string? stackTrace)
    {
        try
        {
            if (message.StartsWith(ValidationReport.Prefix, StringComparison.Ordinal))
            {
                return;
            }

            // 低于警告的消息只保留提到被测 Mod 的，用于追查；其余直接丢弃，避免长时间运行占用内存。
            if (level < LogLevel.Warning && !message.Contains("mugirl", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Pending.Enqueue(new CapturedLog(level, category, message, stackTrace, scope, duringStep));
        }
        catch
        {
            // 日志回调中抛出的异常会回到记录日志的代码（可能是引擎线程）；这里宁可丢一条日志。
        }
    }

    [GeneratedRegex(@"mugirl(race)?\.gamevalidation\w*|__MugirlRace_GameValidation_\w+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OwnNames();

    [GeneratedRegex(@"^(Resolution too small|DrawTexture \(Graphics\.cs:\d+\): Stub!|Deferred loading: \d+ textures have no pixels|Persistent allocation 0x[0-9A-Fa-f]+ .* was not freed before shutdown|Brutal Graphics\.DrawMeshNow is not supported yet)", RegexOptions.CultureInvariant)]
    private static partial Regex HostNoise();
}
