namespace Mugirl.SaveUpgrade;

/// <summary>旧版 Mugirl LordJob 类型的状态图处理方式。</summary>
/// <param name="Type">旧的完整类型名。</param>
/// <param name="Step">认领该类型的步骤 id（<see cref="LordJobPolicy.Claimed"/> 时必填）。</param>
/// <param name="Schema">引入这一行的存档结构版本：来源版本小于它的存档才应用（见 <see cref="UpgradeModuleBuilder.ForSchema"/>）。</param>
internal sealed record LordJobPolicyRow(string Type, LordJobPolicy Policy, string? Step, string Module, int Schema);
