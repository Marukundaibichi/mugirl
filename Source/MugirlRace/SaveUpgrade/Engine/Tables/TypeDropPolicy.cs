namespace Mugirl.SaveUpgrade;

/// <summary>带有已删除类型 <c>Class</c> 的元素如何处理。</summary>
internal enum TypeDropPolicy
{
    /// <summary>
    /// 从容器中移除元素：可移除容器中直接删除，字典中成对删除，并行列表中按下标一起删除，其他位置置空。
    /// </summary>
    RemoveElement,

    /// <summary>无论位置都置空（<c>IsNull="True"</c>），保留列表下标。</summary>
    NullOut,
}
