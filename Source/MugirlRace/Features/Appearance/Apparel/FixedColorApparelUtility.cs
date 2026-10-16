using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Appearance;

// 固色服装（apparel.tags 含 MugirlFixedColor）的公共判定与造型台列表清理。
// 颜色锁定在 Harmony_Apparel_DrawColor_FixedColor；造型台染色入口剔除在 Harmony_StylingStationFixedColor。
internal static class FixedColorApparelUtility
{
    internal const string Tag = "MugirlFixedColor";

    internal static bool IsFixedColor([NotNullWhen(true)] Apparel? apparel)
    {
        List<string>? tags = apparel?.def?.apparel?.tags;
        return tags is not null && tags.Contains(Tag);
    }

    // 从造型台的服装染色字典中移除固色成员；原版在构造函数与 Reset 中只按 CompColorable 过滤。
    internal static void RemoveFixedColorEntries(Dictionary<Apparel, Color>? colors)
    {
        if (colors is null || colors.Count == 0)
        {
            return;
        }

        List<Apparel>? removal = null;
        foreach (KeyValuePair<Apparel, Color> pair in colors)
        {
            if (IsFixedColor(pair.Key))
            {
                removal ??= new List<Apparel>();
                removal.Add(pair.Key);
            }
        }

        if (removal is null)
        {
            return;
        }

        for (int i = 0; i < removal.Count; i++)
        {
            colors.Remove(removal[i]);
        }
    }
}
