namespace Mugirl.GameValidation.Core;

/// <summary><see cref="SaveNameMap.LabelFate(System.Xml.Linq.XElement, string, ISaveOwnerContext)"/> 的结论。</summary>
/// <param name="Kind">处理方式。</param>
/// <param name="NewLabel">改名后的标签（<see cref="SaveLabelFateKind.Renamed"/>）。</param>
/// <param name="Step">消费该标签的步骤（<see cref="SaveLabelFateKind.Consumed"/>，清单给出时）。</param>
/// <param name="Owner">匹配的标签行的 owner 选择器文本（<see cref="SaveLabelFateKind.Unchanged"/> 时为空串）。</param>
internal readonly record struct SaveLabelFate(SaveLabelFateKind Kind, string? NewLabel, string? Step, string Owner)
{
    public static SaveLabelFate Unchanged { get; } = new(SaveLabelFateKind.Unchanged, null, null, string.Empty);

    /// <summary>标签在升级后的存档中不再出现（删除或被消费）。</summary>
    public bool IsGone => Kind is SaveLabelFateKind.Dropped or SaveLabelFateKind.Consumed;

    public override string ToString() => Kind switch
    {
        SaveLabelFateKind.Renamed => $"renamed to {NewLabel} ({Owner})",
        SaveLabelFateKind.Dropped => $"dropped ({Owner})",
        SaveLabelFateKind.Consumed => $"consumed by {Step ?? "a step"} ({Owner})",
        SaveLabelFateKind.Ambiguous => $"ambiguous ({Owner})",
        _ => "unchanged",
    };
}
