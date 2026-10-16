using System.IO;
using System.Threading;

namespace Mugirl.SaveUpgrade;

/// <summary>Mod 构造函数中的升级入口。</summary>
public static class SaveUpgradeBootstrap
{
    // StaticCacheLifecycle: 进程级启动标志，Mod 构造时写入一次。
    private static int initialized;

    /// <summary>
    /// 升级设置文件并安排启动检查。必须在 Mod 构造函数中、<c>GetSettings</c> 之前调用：设置文件在那里第一次被读取。
    /// 不调用翻译（语言数据库尚未加载），不打补丁（补丁由 HarmonyBootstrap 随后统一应用）。
    /// </summary>
    public static void Initialize(Mod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (Interlocked.Exchange(ref initialized, 1) != 0)
        {
            return;
        }

        if (!UpgradeTables.TryGetDefault(out UpgradeTables? tables, out Exception? error))
        {
            MugirlLog.Exception("Save upgrade tables are invalid; settings and saves from older versions will not be upgraded.", error);
        }
        else
        {
            UpgradeSettings(mod, tables);
        }

        // 补丁在 Mod 构造函数中应用；加载长事件结束时它们都已就位，此时检查安装情况。
        LongEventHandler.ExecuteWhenFinished(SaveUpgradeInstallation.Verify);
    }

    private static void UpgradeSettings(Mod mod, UpgradeTables tables)
    {
        string path = SettingsUpgrade.FilePathFor(mod);
        try
        {
            if (SettingsUpgrade.Run(path, tables) is SettingsUpgradeResult.Upgraded)
            {
                MugirlLog.Message("Upgraded the mod settings file " + path + " to settings schema " + SaveSchema.SettingsCurrent + ".");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MugirlLog.Exception("Could not upgrade the mod settings file " + path + "; renamed settings fall back to their defaults.", exception);
        }
    }
}
