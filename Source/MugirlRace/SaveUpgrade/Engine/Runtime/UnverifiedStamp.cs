namespace Mugirl.SaveUpgrade;

/// <summary>读入的存档不能标成当前结构版本的原因；游戏组件保存时记录一次。</summary>
/// <param name="Reason">英文说明（日志用），描述保存时写入的版本戳意味着什么。</param>
/// <param name="IsError">记为错误（升级未完成）还是警告（来自更新版本的存档，保留其版本戳是正确行为）。</param>
internal readonly record struct UnverifiedStamp(string Reason, bool IsError);
