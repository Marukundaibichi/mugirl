namespace Mugirl.Features.Appearance;

/// <summary>设置窗口只发布变化；外观模块负责刷新自己维护的人物绘制缓存。</summary>
[StaticConstructorOnStartup]
internal static class AppearanceSettingsBinding
{
    static AppearanceSettingsBinding() => SettingsWindow.Changed += ExtraOutlineRenderer.SettingsChanged;
}
