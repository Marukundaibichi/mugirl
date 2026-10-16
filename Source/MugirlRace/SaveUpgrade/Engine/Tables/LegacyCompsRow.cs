namespace Mugirl.SaveUpgrade;

/// <summary>旧版构建中某个 Mugirl def 带有的组件类（旧类型名）；组件选择器按这个旧关系判断，而不是当前 DefDatabase。</summary>
/// <param name="Root"><c>ThingDef</c> 或 <c>HediffDef</c>。</param>
/// <param name="DefName">存档中出现的旧 defName。</param>
/// <param name="Schema">这个关系对来源版本低于它的存档成立（默认 1：没有版本戳的旧档）。</param>
internal sealed record LegacyCompsRow(string Root, string DefName, IReadOnlyList<string> CompClasses, string Module, int Schema);
