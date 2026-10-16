namespace Mugirl.SaveUpgrade;

/// <summary>一次读档的升级结果。</summary>
internal enum UpgradeState
{
    /// <summary>存档已是当前结构版本，没有改动。</summary>
    NotLegacy,

    /// <summary>旧存档已升级完成。</summary>
    Upgraded,

    /// <summary>升级中途失败；已应用的步骤保留，存档版本戳不会写成当前版本。</summary>
    Failed,

    /// <summary>存档来自更新的 Mugirl；不做任何改动。</summary>
    Newer,

    /// <summary>旧存档已升级完成，其中 MooGirl 时代（≤0.4.4）的内容不受支持，已丢弃。</summary>
    MooGirlEra,

    /// <summary>存档没有使用本 Mod（Mod 新加入已有存档）。</summary>
    NoMugirl,
}
