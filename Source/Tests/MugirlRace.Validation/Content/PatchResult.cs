namespace MugirlRace.Validation.Content;

/// <summary>一个顶层补丁操作的结果。</summary>
/// <param name="Pack">Patches 目录所属的内容包。</param>
/// <param name="File">补丁文件。</param>
/// <param name="Operation">操作类与 XPath，用于消息。</param>
/// <param name="Succeeded">游戏不会记录 "Patch operation ... failed"。</param>
/// <param name="Problem">无法按所写方式应用时的细节。</param>
internal sealed record PatchResult(ContentPack Pack, string File, string Operation, bool Succeeded, string? Problem);
