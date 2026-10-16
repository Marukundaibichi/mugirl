namespace Mugirl.SaveUpgrade;

/// <summary>保存枚举值的位置：所属元素的标签中由 <see cref="Part"/> 指定的部分（标签文本或列表项、字典键、字典值）。</summary>
/// <param name="Label">旧档中的标签名：表格步骤在同一次计划中改名标签，读取的是改名前的存档。</param>
/// <param name="Flags">[Flags] 枚举按 <c>", "</c> 拆分后逐项改名。</param>
internal sealed record EnumSite(OwnerSelector Owner, string Label, string Enum, bool Flags, ValuePart Part, string Module);
