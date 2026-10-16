using Mugirl.Platform.Rendering;

namespace Mugirl.Rendering;

/// <summary>
/// 开发者模式下启动时检查本模组的原生着色器。1.7 中着色器损坏不会让加载失败，只在首次绘制时回退或显示品红；
/// 这里把未注册与布局错误提前到启动日志。Slang 编译错误只进引擎日志，由离线 ShaderCheck 与游戏内验证负责。
/// </summary>
[StaticConstructorOnStartup]
internal static class ShaderStartupCheck
{
    // ShaderDefRegistry.RegisterAll 在 StaticConstructorOnStartup 之前执行（PlayDataLoader.DoPlayLoad）。
    static ShaderStartupCheck()
    {
        if (Prefs.DevMode)
        {
            ShaderSelfTest.Run();
        }
    }
}
