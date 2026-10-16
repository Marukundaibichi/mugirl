using AlienRace;
using HarmonyLib;
using Mugirl.Platform.Ui;

namespace Mugirl.Compatibility.HumanoidAlienRaces;

// HAR 的造型台直接改写附件变体与颜色，不会让 Pawn 的渲染树失效。在它的输入处理前后比较，
// 联动的变体与颜色一起刷新图形和头像。
// 编译目标方法会运行 HAR StylingStation 的静态构造函数（加载 UI 贴图），所以在加载长事件结束后才打补丁（Deferred）。
[HarmonyPatch(typeof(StylingStation), nameof(StylingStation.DoRaceTabs))]
[MugirlPatch(PatchModule.HumanoidAlienRaces, PatchRisk.Low, "HAR styling selections retain their original behavior and may remain visually stale until another graphics refresh.", Deferred = true)]
internal static class Harmony_StylingStation_DoRaceTabs
{
    internal static void Prefix(Pawn? ___pawn, AlienPartGenerator.AlienComp? ___alienComp, out AppearanceSnapshot? __state)
    {
        __state = null;

        // 每帧都有的 Layout/Repaint 不做快照，也不重建图形。阶段取本次窗口绘制开始时的事件类型，
        // 不受其他控件 Use() 影响；没有 GUI 事件时跳过。
        if (!UiEvent.Exists || GuiPhase.IsLayout || GuiPhase.IsRepaint
            || !MugirlIdentity.IsMugirlPawn(___pawn) || ___alienComp is null)
        {
            return;
        }

        __state = new AppearanceSnapshot(___alienComp);
    }

    internal static void Postfix(Pawn? ___pawn, AlienPartGenerator.AlienComp? ___alienComp, AppearanceSnapshot? __state)
    {
        if (__state is null || ___pawn is null || ___alienComp is null || !__state.HasChanged(___alienComp))
        {
            return;
        }

        ___pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        PortraitsCache.SetDirty(___pawn);
    }

    /// <summary>HAR 外观数据的值拷贝。</summary>
    internal sealed class AppearanceSnapshot
    {
        private readonly int[] variants;
        private readonly List<Color?> addonColors = [];
        private readonly Dictionary<string, Color[]> channels = [];

        internal AppearanceSnapshot(AlienPartGenerator.AlienComp comp)
        {
            variants = comp.addonVariants.ToArray();
            foreach (var color in comp.addonColors)
            {
                addonColors.Add(color.first);
                addonColors.Add(color.second);
            }

            // HAR 就地修改颜色元组，只保存引用会漏掉修改，所以复制颜色值。
            foreach (var channel in comp.ColorChannels)
            {
                channels.Add(channel.Key, [channel.Value.first, channel.Value.second]);
            }
        }

        internal bool HasChanged(AlienPartGenerator.AlienComp comp)
        {
            if (variants.Length != comp.addonVariants.Count
                || addonColors.Count != comp.addonColors.Count * 2
                || channels.Count != comp.ColorChannels.Count)
            {
                return true;
            }

            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i] != comp.addonVariants[i])
                {
                    return true;
                }
            }

            for (int i = 0; i < comp.addonColors.Count; i++)
            {
                if (!SameColor(addonColors[i * 2], comp.addonColors[i].first)
                    || !SameColor(addonColors[(i * 2) + 1], comp.addonColors[i].second))
                {
                    return true;
                }
            }

            foreach (var channel in comp.ColorChannels)
            {
                if (!channels.TryGetValue(channel.Key, out Color[]? colors)
                    || !UnityColor.ExactlyEqual(colors[0], channel.Value.first)
                    || !UnityColor.ExactlyEqual(colors[1], channel.Value.second))
                {
                    return true;
                }
            }

            return false;
        }

        // 判断“是否被改过”用逐分量精确比较；任何分量变化都触发一次刷新，避免容差吞掉微小的编辑。
        private static bool SameColor(Color? a, Color? b)
            => a.HasValue ? b.HasValue && UnityColor.ExactlyEqual(a.Value, b.Value) : !b.HasValue;
    }
}
