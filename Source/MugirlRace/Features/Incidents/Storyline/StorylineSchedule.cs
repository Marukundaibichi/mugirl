namespace Mugirl.Features.Incidents;

// 剧情调度的纯计算部分：只在有事可做的 tick 唤醒 StorylineService。
internal static class StorylineSchedule
{
    internal const int NeverTick = int.MaxValue;

    /// <summary>tick 相加，结果在 int.MaxValue 处饱和（不溢出为负数）。currentTick 为非负游戏 tick。</summary>
    internal static int SafeAddTicks(int currentTick, int delayTicks)
    {
        return delayTicks > int.MaxValue - currentTick ? int.MaxValue : currentTick + delayTicks;
    }

    /// <summary>取较早的唤醒延迟；已到期或过期的计时器按 1 tick 计，下一 tick 处理。</summary>
    internal static void IncludeWakeDelay(ref int currentDelayTicks, int candidateTicks)
    {
        if (candidateTicks <= 0)
        {
            candidateTicks = 1;
        }

        if (candidateTicks < currentDelayTicks)
        {
            currentDelayTicks = candidateTicks;
        }
    }

    /// <summary>由最早的延迟得到下一次唤醒 tick；没有任何待办时永不唤醒（直到 WakeStoryService）。</summary>
    internal static int NextWakeTick(int currentTick, int delayTicks)
    {
        return delayTicks == int.MaxValue ? NeverTick : SafeAddTicks(currentTick, delayTicks);
    }

    /// <summary>倒计时计时器：未到期返回 false，计时器扣除经过的 tick。</summary>
    internal static bool AdvanceTimer(ref int timer, int elapsedTicks)
    {
        if (timer <= 0)
        {
            return true;
        }

        timer -= elapsedTicks;
        return timer <= 0;
    }
}
