namespace Mugirl.Features.Lances;

/// <summary>骑枪冲锋冷却的纯计算，不读取游戏状态。</summary>
internal static class LanceChargeCooldown
{
    /// <summary>距离冷却结束的剩余 tick，已结束为 0。</summary>
    internal static int TicksLeft(int cooldownUntilTick, int currentTick) => Math.Max(0, cooldownUntilTick - currentTick);

    /// <summary>Gizmo 冷却进度：0 为刚开始冷却，1 为就绪；冷却时长不为正时视为就绪。</summary>
    internal static float Percent(int cooldownUntilTick, int cooldownTicks, int currentTick)
    {
        if (cooldownTicks <= 0)
        {
            return 1f;
        }

        return UnityMathf.Clamp01(1f - (float)TicksLeft(cooldownUntilTick, currentTick) / cooldownTicks);
    }
}
