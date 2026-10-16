namespace Mugirl.SaveUpgrade;

/// <summary>存档结构版本号与写入存档的固定名称。</summary>
/// <remarks>
/// 存档中没有版本戳但 <c>meta/modIds</c> 含本 Mod 的，视为版本 0（全部旧版构建的并集，各步骤按 XML 形状判断是否适用）。
/// 以后每次改动存档结构都把 <see cref="Current"/> 加一，并为新版本号登记步骤。下面的名称一旦发布就永久不变。
/// </remarks>
internal static class SaveSchema
{
    /// <summary>当前构建写入的存档结构版本。</summary>
    internal const int Current = 1;

    /// <summary>新游戏的来源版本（没有可升级的来源）。</summary>
    internal const int NewGame = -1;

    /// <summary>版本戳的 Scribe 标签；读取时按这个子元素识别，不依赖所在组件的 Class。</summary>
    internal const string StampLabel = "mugirlSaveSchema";

    /// <summary>写版本戳的 GameComponent 的存档类名。</summary>
    internal const string HubTypeName = "Mugirl.Core.GameComponent_Mugirl";

    /// <summary>Mod 设置文件中的结构版本标签。</summary>
    internal const string SettingsStampLabel = "settingsSchema";

    /// <summary>
    /// 当前构建的设置文件结构版本（与存档结构版本分开编号；没有 <see cref="SettingsStampLabel"/> 的设置文件是版本 0）。
    /// 设置类保存时用 <c>forceSave</c> 写这个常量；设置升级行的版本不得超过它。
    /// </summary>
    internal const int SettingsCurrent = 1;

    /// <summary>本 Mod 在 <c>meta/modIds</c> 中的 packageId（RimWorld 存小写；创意工坊副本带 <c>_steam</c> 后缀）。</summary>
    internal static readonly string[] PackageIds = ["har.mugirlrace", "har.mugirlrace_steam"];
}
