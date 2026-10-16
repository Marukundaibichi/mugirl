namespace Mugirl.GameValidation.Core;

/// <summary>升级清单（K1）中的一个步骤。</summary>
/// <param name="Id">步骤 id。</param>
/// <param name="Phase">阶段：dom、A、B、C。</param>
/// <param name="Module">所属模块。</param>
/// <param name="Consumes">声明消费的旧名（<c>type:</c>、<c>def:</c>、<c>label:</c>、<c>enum:</c>、<c>value:</c> 前缀）。</param>
internal sealed record SaveUpgradeStepRow(string Id, string Phase, string Module, IReadOnlyList<string> Consumes);
