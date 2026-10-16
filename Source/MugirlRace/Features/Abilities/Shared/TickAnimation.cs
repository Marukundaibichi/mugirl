namespace Mugirl.Features.Abilities;

// 由游戏 tick 驱动的周期动画。后期 tick 达到千万级时，单精度的 tick × 系数只剩几度到几十度的分辨率，
// 画面会抖动；这里先在整数域取模或以双精度相乘，再转成单精度。
internal static class TickAnimation
{
    /// <summary>
    /// 每 tick 转 <paramref name="degreesPerTick"/> 度、每 <paramref name="ticksPerTurn"/> tick 恰好转满一圈时的角度，范围 [0, 360)。
    /// 与 <c>(tick * degreesPerTick) % 360</c> 在单精度可精确表示的范围内逐位相同。
    /// </summary>
    internal static float WrappedDegrees(int tick, int ticksPerTurn, float degreesPerTick)
    {
        int phase = tick % ticksPerTurn;
        if (phase < 0)
        {
            phase += ticksPerTurn;
        }

        return phase * degreesPerTick;
    }

    /// <summary>sin(tick × <paramref name="radiansPerTick"/>)，乘积以双精度计算。</summary>
    internal static float Sin(int tick, float radiansPerTick) => (float)Math.Sin(tick * (double)radiansPerTick);
}
