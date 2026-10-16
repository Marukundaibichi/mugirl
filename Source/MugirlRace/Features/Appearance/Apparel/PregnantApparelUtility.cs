using System.Diagnostics.CodeAnalysis;
using Mugirl.Rendering;

namespace Mugirl.Features.Appearance;

// 怀孕变体服装（apparel.tags 含 MugirlPregnantSwap，如金色比基尼）：
// 穿戴者怀孕期间穿着贴图路径追加 _Pregnant（形如 ..._Pregnant_Female_east），
// 使用画师提供的怀孕变体贴图；分娩、流产或终止妊娠（PregnancyUtility 走 RemoveHediff）后换回原图。
internal static class PregnantApparelUtility
{
    internal const string Tag = "MugirlPregnantSwap";
    internal const string VariantSuffix = "_Pregnant";

    internal static bool IsSwapApparel([NotNullWhen(true)] Apparel? apparel)
    {
        List<string>? tags = apparel?.def?.apparel?.tags;
        return tags is not null && tags.Contains(Tag);
    }

    internal static bool IsPregnant([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn is not null
            && HediffDefOf.PregnantHuman is not null
            && pawn.health?.hediffSet is { } hediffSet
            && hediffSet.HasHediff(HediffDefOf.PregnantHuman);
    }

    // 穿着贴图在图形重建时才重新读取路径，这里立即脏化。雪牛娘另外走延后刷新兜底（其中已包含一次立即脏化），
    // 覆盖 HAR 渲染树重建滞后的情况；非雪牛娘穿戴者只依赖立即脏化。
    internal static void NotifyPregnancyChanged(Pawn? pawn)
    {
        if (pawn is null || pawn.Destroyed)
        {
            return;
        }

        if (MugirlIdentity.IsMugirlPawn(pawn))
        {
            PawnRenderRefresh.NotifyPawnChanged(pawn);
        }
        else
        {
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }
}
