namespace MugirlRace.Validation.Content;

/// <summary>统一 Def 文档中节点的来源。</summary>
/// <param name="Pack">内容包。</param>
/// <param name="File">Def 文件；补丁插入的节点为补丁文件。</param>
/// <param name="LoadFolder">Def 文件所在加载目录（补丁插入的节点为 <see langword="null"/>）。</param>
/// <param name="InsertedByPatch">节点由补丁操作插入。</param>
internal sealed record NodeSource(ContentPack Pack, string File, string? LoadFolder, bool InsertedByPatch);
