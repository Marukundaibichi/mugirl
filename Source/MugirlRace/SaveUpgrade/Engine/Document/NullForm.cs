namespace Mugirl.SaveUpgrade;

/// <summary>
/// 叶元素置空时的写法，由读取它的 Scribe 方法决定。深层对象（有子元素、<c>Class</c> 或 <c>IsNull</c> 属性）总是换成
/// 不带子节点的 <c>IsNull="True"</c> 元素，与这里的选择无关。
/// </summary>
/// <remarks>
/// 1.7.4977 反编译：<c>ScribeExtractor.ValueFromNode</c> 只认 <c>IsNull</c> 属性，文本 <c>null</c> 会读成字符串 "null"
/// 或解析失败；<c>ScribeExtractor.DefFromNode</c> 只认文本 <c>null</c>，<c>IsNull</c> 空元素会被当成空 defName 报错；
/// 引用（<c>Scribe_References</c>、<c>LookMode.Reference</c>）同样以文本 <c>null</c> 表示空引用。
/// </remarks>
internal enum NullForm
{
    /// <summary>文本 <c>null</c>：<c>Scribe_References</c>、<c>Scribe_Defs</c> 以及 <c>LookMode.Reference</c>/<c>LookMode.Def</c> 的集合项。</summary>
    Reference,

    /// <summary>不带文本的 <c>IsNull="True"</c> 元素：<c>Scribe_Values</c> 与 <c>LookMode.Value</c> 的集合项（读成 <c>default(T)</c>）。</summary>
    Value,
}
