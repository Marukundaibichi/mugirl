using System.Text.RegularExpressions;
using MugirlRace.Validation.Infrastructure;
using MugirlRace.Validation.Reporting;
using MugirlRace.Validation.Runtime;

namespace MugirlRace.Validation.Checks;

/// <summary>
/// B. 验证运行 Mod 代码期间（补丁解析、Transpiler、JIT、Harmony）没有写出任何警告或错误。
/// </summary>
/// <remarks>
/// <para>
/// 与 hk 不同，Mugirl 在 Mod 构造函数中打补丁，而 RimWorld 1.7 在加载线程上运行 Mod 构造函数（<c>PlayDataLoader.LoadAllPlayData</c>
/// 以 <c>doAsynchronously: true</c> 排队）。因此补丁代码触发的“Tried to use an uninitialized DefOf”与“from a different thread”资源错误
/// 在游戏中同样会出现，不豁免；只有标为 <c>[MugirlPatch(Deferred = true)]</c> 的补丁类在加载结束后于主线程注册，它们的线程错误才是离线假象。
/// “Used UnityData before it's initialized”只在离线时出现（游戏在加载 Mod 之前就初始化了 UnityData），记为信息。
/// </para>
/// <para>其他记录必须修复，或以“正则&lt;TAB&gt;理由”写进 <c>Baselines/log.allow.txt</c>。</para>
/// </remarks>
internal static class LogCheck
{
    private const string MetadataAttribute = "Mugirl.Core.MugirlPatchAttribute";

    public static void Run(Report report, GameRuntime runtime, string baselineDir)
        => report.Run("B.log", "No Verse.Log warnings or errors while mod code ran", recorder => Check(recorder, runtime, baselineDir));

    private static void Check(CheckRecorder recorder, GameRuntime runtime, string baselineDir)
    {
        AllowList allow = AllowList.Load(baselineDir, "log.allow.txt");
        var patterns = allow.Entries.Keys.Select(key => (Key: key, Regex: new Regex(key, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))).ToList();
        Type? metadata = runtime.Mod.GetType(MetadataAttribute, throwOnError: false);
        int messages = 0;
        int problems = 0;
        foreach (LogEntry entry in LogCapture.Entries)
        {
            string first = entry.Text.Split('\n', 2)[0].Trim();
            if (first.Length > 200)
            {
                first = first[..200] + "…";
            }

            string subject = $"{entry.Phase}: {entry.Level}: {first}";
            if (entry.Level == LogLevel.Message)
            {
                messages++;
                recorder.Info(subject, "Log.Message from " + entry.Origin);
                continue;
            }

            problems++;
            if (entry.Text.StartsWith("Used UnityData before it's initialized", StringComparison.Ordinal))
            {
                recorder.Info(subject, $"Offline artifact (from {entry.Origin}): the game initializes UnityData before it loads mods.");
                continue;
            }

            bool otherThread = entry.Text.Contains("from a different thread", StringComparison.Ordinal);
            if (otherThread && IsDeferredPatchCode(entry.OriginType, metadata))
            {
                recorder.Info(subject, $"Offline artifact (from {entry.Origin}): a deferred patch class is registered on the main thread in game.");
                continue;
            }

            string? key = patterns.FirstOrDefault(p => p.Regex.IsMatch(entry.Text)).Key;
            if (key is not null && allow.Allows(key))
            {
                recorder.Info(subject, "Allowed by Baselines/log.allow.txt: " + allow.ReasonFor(key));
                continue;
            }

            recorder.Fail(subject, $"Logged by {entry.Origin}: {entry.Text}" + (otherThread
                ? " — Mugirl patches in the Mod constructor, which RimWorld runs on the loading thread, so this happens in game too: load the asset in a "
                    + "[StaticConstructorOnStartup] type, or mark the patch class [MugirlPatch(..., Deferred = true)]."
                : ""));
        }

        allow.ReportProblems(recorder);
        recorder.Summary = $"{problems} warning/error and {messages} message entr(y/ies) captured";
    }

    // 记录来自延迟注册的补丁类（或其嵌套类型、编译器生成的闭包）。
    private static bool IsDeferredPatchCode(Type? origin, Type? metadata)
    {
        for (Type? current = origin; current is not null && metadata is not null; current = current.DeclaringType)
        {
            object? attribute = current.GetCustomAttributes(metadata, inherit: false).FirstOrDefault();
            if (attribute is not null && metadata.GetProperty("Deferred")?.GetValue(attribute) is true)
            {
                return true;
            }
        }

        return false;
    }
}
