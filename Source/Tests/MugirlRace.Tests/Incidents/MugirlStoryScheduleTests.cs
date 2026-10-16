using Mugirl.Features.Incidents;

namespace Mugirl.Tests.Incidents;

/// <summary><see cref="StorylineSchedule"/>：剧情调度的唤醒 tick 计算与倒计时。</summary>
public sealed class MugirlStoryScheduleTests
{
    [Theory]
    [InlineData(100, 50, 150)]
    [InlineData(0, int.MaxValue, int.MaxValue)]
    [InlineData(int.MaxValue - 5, 10, int.MaxValue)]
    [InlineData(int.MaxValue - 10, 10, int.MaxValue)]
    [InlineData(int.MaxValue - 11, 10, int.MaxValue - 1)]
    public void SafeAddTicks_SaturatesAtMaxValue(int current, int delay, int expected)
        => Assert.Equal(expected, StorylineSchedule.SafeAddTicks(current, delay));

    [Theory]
    [InlineData(int.MaxValue, 500, 500)]
    [InlineData(300, 500, 300)]
    [InlineData(300, 0, 1)]
    [InlineData(300, -40, 1)]
    [InlineData(1, 1, 1)]
    public void IncludeWakeDelay_KeepsEarliestAndTreatsDueTimersAsNextTick(int current, int candidate, int expected)
    {
        int delay = current;
        StorylineSchedule.IncludeWakeDelay(ref delay, candidate);
        Assert.Equal(expected, delay);
    }

    [Fact]
    public void NextWakeTick_NeverWakesWithoutPendingWork()
    {
        Assert.Equal(StorylineSchedule.NeverTick, StorylineSchedule.NextWakeTick(1234, int.MaxValue));
        Assert.Equal(1734, StorylineSchedule.NextWakeTick(1234, 500));
    }

    [Theory]
    [InlineData(0, 5, true, 0)]
    [InlineData(-1, 5, true, -1)]
    [InlineData(10, 3, false, 7)]
    [InlineData(10, 10, true, 0)]
    [InlineData(3, 5, true, -2)]
    public void AdvanceTimer_CountsDownOnlyWhilePositive(int timer, int elapsed, bool expectedDue, int expectedTimer)
    {
        bool due = StorylineSchedule.AdvanceTimer(ref timer, elapsed);
        Assert.Equal(expectedDue, due);
        Assert.Equal(expectedTimer, timer);
    }
}
