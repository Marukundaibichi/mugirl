namespace MugirlRace.Validation.Content;

/// <summary>贴图查找结果。</summary>
internal enum TextureMatch
{
    /// <summary>在总会随 Mod 加载的内容中找到。</summary>
    Found,

    /// <summary>只存在于可选 DLC 或 Mod 中（没装的玩家缺失）。</summary>
    OptionalContentOnly,

    /// <summary>只有忽略大小写才能在本体资源中找到。</summary>
    CaseMismatch,

    /// <summary>只有忽略大小写才能在 Mod 内容中找到：1.7 区分大小写查找 Mod 贴图，游戏里会缺图。</summary>
    ContentCaseMismatch,

    /// <summary>哪里都找不到。</summary>
    Missing,
}
