namespace Mugirl.SaveUpgrade;

/// <summary>旧 Scribe 标签的去向。</summary>
internal enum LabelDisposition
{
    /// <summary>在所属元素内改名。</summary>
    Rename,

    /// <summary>删除（读档时回到新代码的默认值）。</summary>
    Drop,

    /// <summary>由登记的步骤读取并改写成新结构，表格本身不动它。</summary>
    Consumed,
}
