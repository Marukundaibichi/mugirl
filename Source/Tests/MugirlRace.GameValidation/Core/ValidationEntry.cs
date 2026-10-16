namespace Mugirl.GameValidation.Core;

/// <summary>测试模组的入口：加载完成后（StaticConstructorOnStartup 阶段）启动会话。</summary>
/// <remarks>
/// 命令行没有 <c>-mugirlvalidate</c> 时什么也不做，所以这个程序集即使被误装也不会影响正常游戏。
/// 启用时还要求临时存档目录中有脚本写入的参数文件（<see cref="ValidationOptions"/>），玩家的存档目录里不会有它。
/// </remarks>
[StaticConstructorOnStartup]
internal static class ValidationEntry
{
    static ValidationEntry()
    {
        if (!GenCommandLine.CommandLineArgPassed(ValidationOptions.CommandLineSwitch))
        {
            return;
        }

        try
        {
            ValidationSession.Start();
        }
        catch (Exception ex)
        {
            // 静态构造函数中的异常会让 RimWorld 记录 TypeInitializationException 并继续加载，测试则永远不会结束；
            // 这里写出结果行，由脚本按“没有 DONE”判定失败并在超时后结束进程。
            Log.Error($"{ValidationReport.Prefix} FAIL setup: the session could not start: {ex}");
        }
    }
}

/// <summary>只为尽早订阅引擎日志的 Mod 类。</summary>
/// <remarks>
/// Mod 构造发生在加载程序集之后、加载 Def 与贴图之前，比 StaticConstructorOnStartup 早得多；从这里订阅 LogStream，
/// 加载阶段的引擎日志（Shader、贴图解码等不经过 Verse.Log 的消息）也能被检查。没有设置页（SettingsCategory 为空），
/// 不读写设置文件。RimWorld 通过反射创建它，类型与构造函数都保持公有。
/// </remarks>
public sealed class GameValidationMod : Mod
{
    public GameValidationMod(ModContentPack content)
        : base(content)
    {
        if (GenCommandLine.CommandLineArgPassed(ValidationOptions.CommandLineSwitch))
        {
            LogCapture.Install();
        }
    }
}
