namespace Mugirl.GameValidation.Core;

/// <summary>升级清单（K1）中按 owner 选择的一个标签：标签行（改名、删除、消费）、步骤声明消费的标签，或默认值改变的标签。</summary>
/// <param name="Owner">owner 选择器。</param>
/// <param name="Old">旧档中的标签。</param>
/// <param name="New">改名后的标签；删除、消费或默认值行为 null。</param>
/// <param name="Disposition">rename、drop、consumed，或 default（默认值改变，见 <see cref="OldDefault"/>）。</param>
/// <param name="Step">消费它的步骤。</param>
/// <param name="OldDefault">旧构建的默认值（default 行）。</param>
internal sealed record SaveLabelRow(SaveOwnerSelector Owner, string Old, string? New, string Disposition, string? Step, string? OldDefault = null)
{
    public SaveLabelFate Fate => Disposition switch
    {
        "rename" when New is not null && New != Old => new SaveLabelFate(SaveLabelFateKind.Renamed, New, Step, Owner.Text),
        "drop" => new SaveLabelFate(SaveLabelFateKind.Dropped, null, Step, Owner.Text),
        "consumed" => new SaveLabelFate(SaveLabelFateKind.Consumed, null, Step, Owner.Text),
        _ => SaveLabelFate.Unchanged,
    };
}
