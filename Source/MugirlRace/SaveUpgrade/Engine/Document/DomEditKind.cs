namespace Mugirl.SaveUpgrade;

/// <summary>升级计划中的 DOM 原语。</summary>
internal enum DomEditKind
{
    /// <summary>设置属性值（例如改写 <c>Class</c>）。</summary>
    SetAttribute,

    /// <summary>改写叶元素文本。</summary>
    SetText,

    /// <summary>元素改名：新建同属性同子节点的元素并替换旧元素。</summary>
    Rename,

    /// <summary>从父元素中删除。</summary>
    Remove,

    /// <summary>置空：换成同名、不带子节点的 <c>IsNull="True"</c> 元素（深层对象与 <see cref="NullForm.Value"/> 叶元素）。</summary>
    NullOut,

    /// <summary>置空引用：叶元素文本改成 <c>null</c>（<see cref="NullForm.Reference"/>）。</summary>
    NullReference,

    /// <summary>在所属元素末尾追加带文本的子元素。</summary>
    Append,

    /// <summary>挂入计划创建的空容器，保持其他节点身份。</summary>
    AppendElement,

    /// <summary>移动既有元素，不把其中对象的加载 ID 记为删除。</summary>
    Move,
}
