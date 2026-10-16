namespace Mugirl.SaveUpgrade;

/// <summary>旧类型名的去向：改名为 <see cref="New"/>，或按 <see cref="Policy"/> 删除。</summary>
/// <param name="Old">旧的完整类型名（存档写法，比较不区分大小写）。</param>
/// <param name="New">新类型；删除时为 null。</param>
/// <param name="Policy">删除策略；改名时为 null。</param>
/// <param name="Schema">引入这一行的存档结构版本：来源版本小于它的存档才应用（见 <see cref="UpgradeModuleBuilder.ForSchema"/>）。</param>
internal sealed record TypeRow(string Old, Type? New, TypeDropPolicy? Policy, string Module, string Reason, int Schema)
{
    /// <summary>新类型的存档写法；删除时为 null。</summary>
    internal string? NewSaveName { get; } = New is null ? null : SaveTypeNames.Of(New);
}
