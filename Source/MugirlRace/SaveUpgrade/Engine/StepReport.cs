namespace Mugirl.SaveUpgrade;

/// <summary>一个步骤在一次读档中的结果。</summary>
/// <param name="Actions">实际生效的编辑或改动对象数。</param>
/// <param name="Milliseconds">计划与应用的耗时。</param>
internal sealed record StepReport(string Id, UpgradePhase Phase, int Actions, long Milliseconds);
