using HarmonyLib;
using Mugirl.SaveUpgrade;

namespace Mugirl.Core;

// 类的短名（不含命名空间）决定设置文件名 Mod_<文件夹>_MugirlMod.xml，属于公开约定，不能改名。
public class MugirlMod : Mod
{
    // StaticCacheLifecycle: 进程级 Mod 设置；Mod 实例在进程内只构造一次，不随存档清理。
    private static MugirlSettings? settings;

    public MugirlMod(ModContentPack modContentPack) : base(modContentPack)
    {
        ContentRoot = modContentPack.RootDir;
        // 顺序：升级设置文件 → 读设置 → 打补丁 → 加载长事件结束时检查升级钩子（Initialize 用 ExecuteWhenFinished 安排，
        // 在这里同步应用的补丁之后运行）。设置文件第一次被读取之前必须先升级旧键名。
        SaveUpgradeBootstrap.Initialize(this);
        // 先读设置再打补丁：打补丁期间运行的代码（Prepare()、目标类型的静态构造）读取 Settings 时不会拿到 null。
        // 读取设置文件不经过本 Mod 的任何补丁，先读设置不会漏掉补丁的作用。
        settings = GetSettings<MugirlSettings>();
        // 补丁必须在 Mod 构造函数中注册：此时原版调用方大多尚未编译，小 getter 的补丁不会被 JIT 内联绕过。
        HarmonyBootstrap.ApplyAtStartup(new Harmony(HarmonyBootstrap.HarmonyId));
        global::Mugirl.Compatibility.CombatCompatibilityBootstrap.Register();
    }

    /// <summary>Mod 构造之前（例如离线验证）为 null。</summary>
    internal static MugirlSettings? Settings => settings;

    // StaticCacheLifecycle: 进程级 Mod 根目录，Mod 构造时写入一次，不随存档清理。
    /// <summary>Mod 根目录；Mod 构造之前为 null。</summary>
    internal static string? ContentRoot { get; private set; }

    public override string SettingsCategory()
    {
        return "Mugirl.Settings.Category".Translate().ToString();
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        if (settings is not null)
        {
            SettingsWindow.Draw(inRect, settings);
        }

        base.DoSettingsWindowContents(inRect);
    }

    public override void WriteSettings()
    {
        // 设置窗口关闭时一定会写设置；拖动滑条途中暂缓的描边刷新在这里补上。
        SettingsWindow.FlushPendingRefresh();
        base.WriteSettings();
    }
}
