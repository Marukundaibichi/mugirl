namespace Mugirl.SaveUpgrade;

/// <summary>允许其余结构升级继续的兼容诊断；不表示已经恢复丢失的来源对象。</summary>
internal sealed record UpgradeWarning(string StepId, string Message);
