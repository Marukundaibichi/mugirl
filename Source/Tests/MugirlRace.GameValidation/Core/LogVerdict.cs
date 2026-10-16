namespace Mugirl.GameValidation.Core;

/// <summary><see cref="LogPolicy.Classify"/> 对一条日志的结论。</summary>
internal enum LogVerdict
{
    /// <summary>不需要报告：低于该策略关心的级别，或是宿主底噪。</summary>
    Ignored,

    /// <summary>命中评审过的白名单：只计数。</summary>
    Allowed,

    /// <summary>只含未加载 Mod 的记号：列出，不算失败。</summary>
    Foreign,

    /// <summary>默认策略下没有提到被测 Mod 的错误：列出，不算失败。</summary>
    Unattributed,

    /// <summary>失败。</summary>
    Failure,
}
