namespace Mugirl.SaveUpgrade;

/// <summary>值上下文中保存的文本种类，决定用哪张表改写。</summary>
internal enum ValueContextKind
{
    /// <summary>普通字符串（信号名、标签、记录键等），按该上下文的值映射行改写。</summary>
    Text,

    /// <summary>类型名（<c>Type</c> 字段），按类型表改写。</summary>
    Type,

    /// <summary>以字符串保存的 defName（<c>Scribe_Values</c>），按指定根类型的 Def 表改写；删除的 Def 由 Def 步骤移除或置空。</summary>
    Def,

    /// <summary>
    /// <c>Scribe_Defs</c> 或 <c>LookMode.Def</c> 保存的 Def 引用。改名只由 <c>BackCompatibleDefName</c> 后缀处理，DOM 不改写；
    /// 登记它只为删除：删除的 Def 从可移除容器中移除，其他位置文本置空为 <c>null</c>。
    /// </summary>
    DefReference,
}
