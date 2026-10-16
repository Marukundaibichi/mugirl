namespace Mugirl.SaveUpgrade;

/// <summary>按读取顺序重放到独立升级 DOM 的验证事实；字段名是诊断 JSON 协议。</summary>
internal sealed record ValidationReadEvent(string kind, string path, string? oldValue, string? newValue,
    int[]? indices = null, string[]? oldKeys = null, string[]? oldValues = null);
