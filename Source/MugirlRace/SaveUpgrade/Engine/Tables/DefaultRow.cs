namespace Mugirl.SaveUpgrade;

/// <summary>
/// 旧代码在值等于默认值时不写标签；默认值改了以后，旧档中缺失的标签必须补写成旧默认值。
/// 只有所属元素带有 <see cref="Evidence"/> 要求的旧版证据时才补写。
/// </summary>
/// <param name="OldDefault">旧默认值的 Scribe 文本（不变区域格式，布尔为 <c>True</c>/<c>False</c>，枚举为成员名）。</param>
/// <param name="EvidenceLabels"><see cref="LegacyEvidence.LegacyLabel"/> 对应的旧版专有兄弟标签。</param>
/// <param name="Schema">引入这一行的存档结构版本：来源版本小于它的存档才应用（见 <see cref="UpgradeModuleBuilder.ForSchema"/>）。</param>
internal sealed record DefaultRow(OwnerSelector Owner, string Label, string OldDefault, LegacyEvidence Evidence, IReadOnlyList<string> EvidenceLabels, string Module, string Reason, int Schema);
