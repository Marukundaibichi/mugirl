namespace Mugirl.SaveUpgrade;

/// <summary>Mod 设置文件的升级行；文件的 <c>settingsSchema</c> 小于 <see cref="Schema"/> 时应用。</summary>
/// <param name="New">新名称；删除标签时为 null。</param>
/// <param name="Schema">引入这一行的设置结构版本。</param>
internal sealed record SettingsRow(SettingsRowKind Kind, string Old, string? New, int Schema, string Module);
