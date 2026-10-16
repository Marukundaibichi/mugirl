namespace Mugirl.GameValidation.Core;

/// <summary>套件步骤结束时如何评判本步骤捕获的日志。</summary>
internal enum SuiteLogPolicy
{
    /// <summary>提到被测 Mod 的警告与错误记为失败，其余错误只列出（默认）。</summary>
    ModMentions,

    /// <summary>
    /// 任何错误都记为失败，除非命中评审过的白名单或被归为 "foreign"（只含未加载 Mod 的记号）；
    /// 本套件的第一个步骤还评判从主菜单读档期间（范围 "entry"）的日志。存档升级套件使用。
    /// </summary>
    AnyError,
}
