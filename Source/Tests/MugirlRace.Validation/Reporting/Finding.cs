namespace MugirlRace.Validation.Reporting;

/// <summary>检查输出的一行结果。</summary>
/// <param name="Check">产生它的检查 id（例如 <c>B.unity-boundary</c>）。</param>
/// <param name="Severity">对运行结果的影响。</param>
/// <param name="Subject">结果针对的对象，跨运行、跨运行时保持稳定（类型、成员、文件或键）。</param>
/// <param name="Message">给人看的说明；可以含运行时相关的细节，例如异常文本。</param>
/// <param name="Location">已知时给出位置（文件、方法）。</param>
internal sealed record Finding(string Check, Severity Severity, string Subject, string Message, string? Location)
{
    /// <summary>比较不同运行时结果时使用的稳定标识。</summary>
    public string Key => Check + " | " + Subject;
}
