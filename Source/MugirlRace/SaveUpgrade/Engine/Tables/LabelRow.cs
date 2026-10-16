namespace Mugirl.SaveUpgrade;

/// <summary>所属元素内一个旧 Scribe 标签的去向。</summary>
/// <param name="Owner">所属元素选择器（标签是它的直接子元素）。</param>
/// <param name="Old">旧标签。</param>
/// <param name="New">新标签；删除或由步骤处理时为 null。</param>
/// <param name="Step">处理该标签的步骤 id（<see cref="LabelDisposition.Consumed"/> 时必填）。</param>
/// <param name="Schema">引入这一行的存档结构版本：来源版本小于它的存档才应用（见 <see cref="UpgradeModuleBuilder.ForSchema"/>）。</param>
internal sealed record LabelRow(OwnerSelector Owner, string Old, string? New, LabelDisposition Disposition, string? Step, string Module, string Reason, int Schema);
