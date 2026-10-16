namespace Mugirl.SaveUpgrade;

/// <summary>Mod 设置文件升级的结果。</summary>
internal enum SettingsUpgradeResult
{
    /// <summary>表格中没有设置行，不处理设置文件。</summary>
    NoTarget,

    /// <summary>设置文件不存在（首次运行）。</summary>
    NoFile,

    /// <summary>文件的 <c>settingsSchema</c> 已是目标版本。</summary>
    UpToDate,

    /// <summary>已升级并写回。</summary>
    Upgraded,

    /// <summary>文件不是可读的设置 XML，保持原样（原版读取时会回到默认设置）。</summary>
    Unreadable,
}
