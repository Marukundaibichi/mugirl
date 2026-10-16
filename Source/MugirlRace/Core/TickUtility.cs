namespace Mugirl.Core;

internal static class TickUtility
{
    // 表现插值采用 150 倍的步长上限。引擎在 UltraSpeedBoost 时可能返回 20000，
    // 该值会让逐帧插值一帧跳过整段动画，因此在此钳制。
    internal const float MaxPresentationTickRate = 150f;

    internal static void Pause()
    {
        if (Current.ProgramState == ProgramState.Playing)
        {
            Find.TickManager?.Pause();
        }
    }

    internal static bool TryGetCurrentGameTick(out int tick)
    {
        if (Current.ProgramState == ProgramState.Playing && Find.TickManager is { } manager)
        {
            tick = manager.TicksGame;
            return true;
        }

        tick = 0;
        return false;
    }

    internal static int CurrentGameTickOrFallback(int fallback)
    {
        return TryGetCurrentGameTick(out int tick) ? tick : fallback;
    }

    /// <summary>当前倍速，用于表现层逐帧插值；暂停或不在游戏中时返回 false。结果不超过 <see cref="MaxPresentationTickRate"/>。</summary>
    internal static bool TryGetPresentationTickRate(out float tickRateMultiplier)
    {
        tickRateMultiplier = 0f;
        if (Current.ProgramState != ProgramState.Playing)
        {
            return false;
        }

        TickManager? manager = Find.TickManager;
        if (manager is null || manager.Paused)
        {
            return false;
        }

        tickRateMultiplier = UnityMathf.Min(manager.TickRateMultiplier, MaxPresentationTickRate);
        return tickRateMultiplier > 0f;
    }
}
