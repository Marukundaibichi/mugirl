namespace Mugirl.SaveUpgrade;

/// <summary>保存字符串、类型名或 defName 的位置；值映射与 Def 删除只作用于登记过的上下文。</summary>
/// <param name="Name">上下文名，值映射行用它引用本上下文。</param>
/// <param name="Label">旧档中的标签名：表格步骤在同一次计划中改名标签，读取的是改名前的存档。</param>
/// <param name="DefRoot"><see cref="ValueContextKind.Def"/> 与 <see cref="ValueContextKind.DefReference"/> 时的 DefDatabase 根类型短名。</param>
/// <param name="Part">值所在的部分：标签文本或列表项、字典键、字典值。</param>
internal sealed record ValueContext(string Name, OwnerSelector Owner, string Label, ValueContextKind Kind, string? DefRoot, ValuePart Part, string Module)
{
    /// <summary>上下文中的值只能置空时的写法：Def 引用写文本 <c>null</c>，其余（Scribe_Values 读取）写 <c>IsNull</c>。</summary>
    internal NullForm NullForm => Kind == ValueContextKind.DefReference ? NullForm.Reference : NullForm.Value;

    /// <summary>上下文是否保存 defName（字符串或 Def 引用）。</summary>
    internal bool HoldsDefNames => Kind is ValueContextKind.Def or ValueContextKind.DefReference;
}
