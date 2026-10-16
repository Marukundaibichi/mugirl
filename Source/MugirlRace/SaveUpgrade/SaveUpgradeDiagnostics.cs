using System.Threading;

namespace Mugirl.SaveUpgrade;

/// <summary>最近一次读档的升级报告，供游戏内验证与开发者工具读取。</summary>
public static class SaveUpgradeDiagnostics
{
    // StaticCacheLifecycle: 进程级，每次读档结束时覆盖；只保存字符串，不引用游戏对象。
    private static string? lastReportJson;

    /// <summary>
    /// 最近一次读档的报告 JSON（读档开始时清除，读档结束或中止时发布）：<c>{"session":int,"state":"NotLegacy|Upgraded|Failed|Newer|MooGirlEra|NoMugirl","failedStep":str|null,
    /// "steps":[{"id":str,"phase":"dom|A|B|C","actions":int,"ms":int}],"totalMs":int,"backup":str|null}</c>；本进程尚未读档或读档进行中时为 null。
    /// </summary>
    public static string? LastReportJson => Volatile.Read(ref lastReportJson);

    /// <summary>发布报告；读档开始时以 null 清除上一次的报告。</summary>
    internal static void Publish(string? json) => Volatile.Write(ref lastReportJson, json);
}
