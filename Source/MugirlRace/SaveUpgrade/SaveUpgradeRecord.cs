namespace Mugirl.SaveUpgrade;

/// <summary>
/// 存档中最近一次升级的记录（写在 <c>GameComponent_Mugirl</c> 的 <c>lastUpgrade</c> 下），只用于诊断。
/// 标签是存档结构的一部分，发布后不改名。
/// </summary>
public sealed class SaveUpgradeRecord : IExposable
{
    /// <summary>升级前的存档结构版本。</summary>
    internal int fromSchema;

    /// <summary>升级后的存档结构版本。</summary>
    internal int toSchema;

    /// <summary>实际改动了存档的步骤 id。</summary>
    internal List<string>? stepIds;

    /// <summary>写出原存档的游戏版本。</summary>
    internal string? gameVersion;

    public void ExposeData()
    {
        Scribe_Values.Look(ref fromSchema, "fromSchema", 0);
        Scribe_Values.Look(ref toSchema, "toSchema", 0);
        Scribe_Collections.Look(ref stepIds, "stepIds", LookMode.Value);
        Scribe_Values.Look(ref gameVersion, "gameVersion");
    }
}
