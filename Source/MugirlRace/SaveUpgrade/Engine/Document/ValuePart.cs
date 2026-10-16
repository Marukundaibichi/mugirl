namespace Mugirl.SaveUpgrade;

/// <summary>标签元素中保存值的部分。</summary>
internal enum ValuePart
{
    /// <summary>单值标签自身的文本，或列表的各 <c>li</c> 项。</summary>
    Items,

    /// <summary>Scribe 字典的键（<c>keys/li</c>）。</summary>
    Keys,

    /// <summary>Scribe 字典的值（<c>values/li</c>）。</summary>
    Values,
}
