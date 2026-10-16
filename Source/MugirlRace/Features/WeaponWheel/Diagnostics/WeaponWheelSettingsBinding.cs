namespace Mugirl.Features.WeaponWheel;

/// <summary>把功能自己的诊断动作登记到设置窗口，核心设置不依赖武器轮盘。</summary>
[StaticConstructorOnStartup]
internal static class WeaponWheelSettingsBinding
{
    static WeaponWheelSettingsBinding()
    {
        SettingsWindow.RegisterDeveloperAction("Mugirl.Settings.WeaponWheelDevLog.DumpPatches",
            static settings => settings.enableWeaponWheelDevLog, WeaponWheelDevLog.PrintPatchReport);
    }
}
