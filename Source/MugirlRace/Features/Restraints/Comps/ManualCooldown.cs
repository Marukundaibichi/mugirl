namespace Mugirl.Features.Restraints;

// 高级束具手动按钮的冷却计算（纯函数，可离线测试）。冷却时长至少 1 tick，与 1.6 的 Mathf.Max(1, cooldown) 相同。
internal static class ManualCooldown
{
    internal static int TicksLeft(int lastUseTick, int cooldownTicks, int currentTick)
    {
        return Math.Max(0, lastUseTick + Math.Max(1, cooldownTicks) - currentTick);
    }

    // 冷却进度 0–1；冷却结束为 1。
    internal static float Percent(int ticksLeft, int cooldownTicks)
    {
        return ticksLeft <= 0 ? 1f : UnityMathf.Clamp01(1f - (float)ticksLeft / Math.Max(1, cooldownTicks));
    }
}
