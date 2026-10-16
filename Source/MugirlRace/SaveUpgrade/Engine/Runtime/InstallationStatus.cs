namespace Mugirl.SaveUpgrade;

/// <summary>升级钩子的安装状态。</summary>
internal enum InstallationStatus
{
    /// <summary>启动检查尚未运行。</summary>
    Unverified,

    /// <summary>全部钩子都由本 Mod 的 Harmony 实例安装。</summary>
    Installed,

    /// <summary>有钩子缺失：读档的版本戳不会写成当前版本。</summary>
    Broken,
}
