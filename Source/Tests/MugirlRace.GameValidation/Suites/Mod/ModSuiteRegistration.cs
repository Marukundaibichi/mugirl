using Mugirl.GameValidation.Suites;

namespace Mugirl.GameValidation.Core;

/// <summary>登记 Suites/Mod/ 中引用被测 Mod 类型的套件；-p:MugirlPlumbingOnly=true 的构建不编译本目录。</summary>
internal static partial class SuiteCatalog
{
    static partial void AddModSuites(Dictionary<string, Func<ValidationSuite>> factories)
    {
        factories[StartupSuiteName] = StartupSuite.Create;
        factories[UiSuiteName] = UiSmokeSuite.Create;
        factories[UpgradeSuiteName] = UpgradeSuite.Create;
    }
}
