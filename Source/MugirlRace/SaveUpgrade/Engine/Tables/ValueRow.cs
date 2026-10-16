namespace Mugirl.SaveUpgrade;

/// <summary>文本上下文中一个旧值的去向；<see cref="New"/> 为 null 时删除该值（列表项删除，单值标签删除回到默认值）。</summary>
/// <param name="Schema">引入这一行的存档结构版本：来源版本小于它的存档才应用（见 <see cref="UpgradeModuleBuilder.ForSchema"/>）。</param>
internal sealed record ValueRow(string Context, string Old, string? New, string Module, int Schema);
