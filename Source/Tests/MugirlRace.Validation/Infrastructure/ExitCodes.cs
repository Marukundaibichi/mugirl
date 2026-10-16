namespace MugirlRace.Validation.Infrastructure;

/// <summary>进程退出码。</summary>
internal static class ExitCodes
{
    /// <summary>所有检查通过（警告与信息不导致失败）。</summary>
    public const int Passed = 0;

    /// <summary>至少一项检查报告失败。</summary>
    public const int Failed = 1;

    /// <summary>命令行错误，或环境无法验证（缺少游戏、Mod 或 Harmony 文件）。</summary>
    public const int Usage = 2;
}
