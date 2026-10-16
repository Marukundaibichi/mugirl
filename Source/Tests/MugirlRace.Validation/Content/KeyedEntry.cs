namespace MugirlRace.Validation.Content;

/// <summary>Keyed 翻译键的一处定义。</summary>
/// <param name="Pack">定义它的内容包。</param>
/// <param name="File">文件（tar 包内为 <c>archive!entry</c>）。</param>
/// <param name="InMinimal">只启用 Core 与 Mod 硬依赖时也加载。</param>
/// <param name="Text">翻译文本。</param>
internal sealed record KeyedEntry(ContentPack Pack, string File, bool InMinimal, string Text);
