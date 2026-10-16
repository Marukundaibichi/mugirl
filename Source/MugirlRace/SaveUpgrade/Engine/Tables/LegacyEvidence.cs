namespace Mugirl.SaveUpgrade;

/// <summary>补写旧默认值前要求所属元素带有的旧版证据。没有存档版本戳本身不算证据。</summary>
[Flags]
internal enum LegacyEvidence
{
    None = 0,

    /// <summary>所属元素的 <c>Class</c> 是类型表中的旧类型名。</summary>
    LegacyClass = 1,

    /// <summary>所属物品或健康状态的 <c>def</c> 是 Def 表中的旧名称。</summary>
    LegacyDef = 2,

    /// <summary>所属元素带有登记的旧版专有兄弟标签之一。</summary>
    LegacyLabel = 4,
}
