namespace Mugirl.Features.Appearance;

/// <summary>
/// 身体附件的变体下标，与 Mugirl_Race.xml 中 BodyAccessoryAddon 的贴图顺序一一对应：
/// 0 无、1 牙印、2 屁股掌印、3 条形码、4 私处毛发（只能在梳妆台手选）、5 化学疤痕。
/// </summary>
internal static class BodyAccessoryVariants
{
    internal const int None = 0;
    internal const int LovinBite = 1;
    internal const int LovinHandprint = 2;
    internal const int Barcode = 3;
    internal const int ChemicalScar = 5;

    /// <summary>只有 Lovin 触发的两种印记可以作为临时变体保存。</summary>
    internal static bool IsLovinMark(int variant) => variant == LovinBite || variant == LovinHandprint;

    /// <summary>
    /// 用一次 [0, 1) 掷骰决定 Lovin 印记：低于 <paramref name="biteChance"/> 为牙印，再加上
    /// <paramref name="handprintChance"/> 之内为掌印，其余不留印记。
    /// </summary>
    internal static int SelectLovinMark(float roll, float biteChance, float handprintChance)
    {
        if (roll < biteChance)
        {
            return LovinBite;
        }

        return roll < biteChance + handprintChance ? LovinHandprint : None;
    }

    /// <summary>XML 配置的持续时间区间：下限至少 1 tick，上限不低于下限。</summary>
    internal static (int Min, int Max) NormalizeMarkDuration(int configuredMin, int configuredMax)
    {
        int min = Math.Max(1, configuredMin);
        return (min, Math.Max(min, configuredMax));
    }
}
