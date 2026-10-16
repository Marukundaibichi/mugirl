namespace MugirlRace.Validation.Reporting;

/// <summary>收集一项正在运行的检查的结果。</summary>
internal sealed class CheckRecorder
{
    private readonly List<Finding> findings = [];

    public CheckRecorder(string id) => Id = id;

    public string Id { get; }

    public IReadOnlyList<Finding> Findings => findings;

    public int FailureCount { get; private set; }

    public int WarningCount { get; private set; }

    /// <summary>PASS 行的文字，由检查写明它验证了什么。</summary>
    public string? Summary { get; set; }

    /// <summary>检查无法运行的原因，或 <see langword="null"/>。</summary>
    public string? SkipReason { get; private set; }

    public void Fail(string subject, string message, string? location = null)
    {
        FailureCount++;
        findings.Add(new Finding(Id, Severity.Failure, subject, message, location));
    }

    public void Warn(string subject, string message, string? location = null)
    {
        WarningCount++;
        findings.Add(new Finding(Id, Severity.Warning, subject, message, location));
    }

    public void Info(string subject, string message, string? location = null)
        => findings.Add(new Finding(Id, Severity.Info, subject, message, location));

    /// <summary>标记检查在此环境下不适用或无法进行。</summary>
    public void Skip(string reason) => SkipReason = reason;
}
