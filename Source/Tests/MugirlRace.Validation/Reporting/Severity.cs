namespace MugirlRace.Validation.Reporting;

/// <summary>结果对运行的影响。</summary>
internal enum Severity
{
    /// <summary>给审阅者的背景信息；从不导致失败，也不在运行时之间比较。</summary>
    Info,

    /// <summary>需要人看一眼，但不导致失败。</summary>
    Warning,

    /// <summary>导致运行失败（退出码 1）。</summary>
    Failure,
}
