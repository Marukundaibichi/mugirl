namespace MugirlRace.Validation.Content;

/// <summary>完整配置中存在的一个 Def。</summary>
/// <param name="DefName">defName。</param>
/// <param name="Type">运行时 Def 类型（节点的 <c>Class</c> 或元素名）。</param>
/// <param name="Pack">定义它的内容包。</param>
/// <param name="File">Def 文件（或补丁文件）。</param>
/// <param name="InMinimal">只启用 Core 与 Mod 硬依赖时也存在。</param>
/// <param name="Implied">由游戏从另一个 Def 生成。</param>
internal sealed record DefEntry(string DefName, Type Type, ContentPack Pack, string File, bool InMinimal, bool Implied);
