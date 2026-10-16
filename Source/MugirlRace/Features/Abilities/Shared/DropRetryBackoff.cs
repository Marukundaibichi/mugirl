namespace Mugirl.Features.Abilities;

/// <summary>
/// 载荷放置失败后的重试退避：首次 30 tick，之后逐次翻倍，上限 120 tick。避免放不下的载荷每 tick 重做全径向落点扫描。
/// 不存档；读档后第一次失败重新从 30 tick 开始。
/// </summary>
internal struct DropRetryBackoff
{
    internal const int FirstDelayTicks = 30;
    internal const int MaxDelayTicks = 120;

    private int delayTicks;
    private int notBeforeTick;

    /// <summary>当前退避时长；尚未失败过时为 0。</summary>
    internal readonly int DelayTicks => delayTicks;

    /// <summary>仍处在上次失败后的等待窗口内。</summary>
    internal readonly bool IsWaiting(int now) => notBeforeTick > now;

    /// <summary>记录一次放置失败，从 <paramref name="now"/> 起等待下一段退避时长。</summary>
    internal void Schedule(int now)
    {
        delayTicks = delayTicks <= 0 ? FirstDelayTicks : Math.Min(MaxDelayTicks, delayTicks * 2);
        notBeforeTick = now + delayTicks;
    }
}
