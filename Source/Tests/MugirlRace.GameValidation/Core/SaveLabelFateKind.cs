namespace Mugirl.GameValidation.Core;

/// <summary>升级清单为一个（owner, 标签）给出的处理。</summary>
internal enum SaveLabelFateKind
{
    /// <summary>清单没有匹配的标签行：标签保持原名。</summary>
    Unchanged,

    /// <summary>改名为 <see cref="SaveLabelFate.NewLabel"/>。</summary>
    Renamed,

    /// <summary>删除（drop）。</summary>
    Dropped,

    /// <summary>由步骤 <see cref="SaveLabelFate.Step"/> 消费（移到别处或换成新结构）。</summary>
    Consumed,

    /// <summary>多个标签行选中同一元素且结论不同：清单本身有冲突。</summary>
    Ambiguous,
}
