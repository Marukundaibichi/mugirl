namespace Mugirl.SaveUpgrade;

/// <summary>旧 defName 的去向（按 DefDatabase 根类型区分，同名不同根的 Def 各自登记）。</summary>
/// <param name="Root">DefDatabase 根类型短名，例如 <c>ThingDef</c>。</param>
/// <param name="Old">旧 defName（区分大小写）。</param>
/// <param name="New">新 defName；删除时为 null。</param>
/// <param name="Policy">删除策略；改名时为 null。</param>
/// <param name="DerivedFrom">由原版隐含 Def 规则（<c>Make_</c>、<c>Corpse_</c> 等）派生时，来源行的旧名；登记行为 null。</param>
/// <param name="Schema">引入这一行的存档结构版本：来源版本小于它的存档才应用（见 <see cref="UpgradeModuleBuilder.ForSchema"/>）。</param>
/// <param name="LegacyRoot">自定义 Def 数据库根类型改名时的旧短名，只用于核对旧档语料；运行时始终按 Root 查新数据库。</param>
internal sealed record DefRow(string Root, string Old, string? New, DefDropPolicy? Policy, string Module, string Reason, int Schema, string? DerivedFrom = null, string? LegacyRoot = null);
