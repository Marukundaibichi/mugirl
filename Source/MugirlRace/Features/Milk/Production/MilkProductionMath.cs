namespace Mugirl.Features.Milk;

// 产奶数值的纯计算，不访问游戏状态，便于离线测试。表达式顺序与 1.6 一致，保证同一输入得到同一 float 结果。
internal static class MilkProductionMath
{
    internal const float TicksPerDay = 60000f;
    internal const float SecondsPerDayDisplay = 1000f;

    // 每个活跃 tick 增加的饱满度：resourceAmount 以百分比计（100 = 一整槽），间隔至少 1 tick，倍率不为负。
    internal static float FullnessPerTick(float resourceAmount, float intervalDays, float multiplier)
    {
        float intervalTicks = UnityMathf.Max(1f, intervalDays * TicksPerDay);
        float targetFullnessGain = UnityMathf.Max(0f, resourceAmount) / 100f;
        return targetFullnessGain * UnityMathf.Max(0f, multiplier) / intervalTicks;
    }

    // 结算 elapsedTicks 个活跃 tick 后的饱满度，上限为 1。
    internal static float Accrue(float fullness, float fullnessPerTick, int elapsedTicks)
    {
        fullness += fullnessPerTick * elapsedTicks;
        return fullness > 1f ? 1f : fullness;
    }

    // 乳房 Hediff 的产量倍率。优先级与 1.6 相同：特殊 > 普通 > 小 > 大 > 平坦或男性。
    internal static bool TryGetBreastYieldMultiplier(in BreastProfile profile, out float multiplier)
    {
        if (profile.Special)
        {
            multiplier = 1.5f;
            return true;
        }

        if (profile.Normal)
        {
            multiplier = 1f;
            return true;
        }

        if (profile.Small)
        {
            multiplier = 0.75f;
            return true;
        }

        if (profile.Large)
        {
            multiplier = 1.25f;
            return true;
        }

        if (profile.FlatOrMale)
        {
            multiplier = 0.5f;
            return true;
        }

        multiplier = 0f;
        return false;
    }
}
