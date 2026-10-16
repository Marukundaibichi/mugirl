namespace Mugirl.SaveUpgrade;

/// <summary>深复制的历史布局标签只在当前调用链有效，异常后也恢复上层状态。</summary>
internal readonly record struct LegacyThinkCopyScope(bool PreviousOmittedDuty, bool OmittedDuty, bool PreviousEarlierCondition, bool EarlierCondition);
