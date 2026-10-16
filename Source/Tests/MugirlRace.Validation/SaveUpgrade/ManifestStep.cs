namespace MugirlRace.Validation.SaveUpgrade;

/// <summary>清单中的一个升级步骤及其声明消费的旧名（<c>type:</c>、<c>def:</c>、<c>label:</c>、<c>enum:</c>、<c>value:</c> 令牌）。</summary>
internal sealed record ManifestStep(string Id, string Phase, string Module, IReadOnlyList<string> Consumes, string Reason);
