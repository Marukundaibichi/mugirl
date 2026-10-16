namespace Mugirl.SaveUpgrade;

/// <summary>按名称保存的枚举成员改名。</summary>
/// <param name="Enum">枚举的完整类型名（新名称）。</param>
/// <param name="Schema">引入这一行的存档结构版本：来源版本小于它的存档才应用（见 <see cref="UpgradeModuleBuilder.ForSchema"/>）。</param>
internal sealed record EnumRow(string Enum, string Old, string New, string Module, int Schema);
