using Mugirl.Features.Restraints;

namespace Mugirl.Tests.Restraints;

/// <summary>洗脑表演时间表：结束 tick 取各段结束时刻的最大值，与 1.6 的 GetCompletionTick 相同。</summary>
public sealed class BrainwashPerformanceScheduleTests
{
    [Fact]
    public void CompletionTick_IsAtLeastOne()
    {
        HediffCompProperties_BrainwashPerformance props = new() { triggerTicks = 0, stunDelayTicks = 0, stunDurationTicks = 0 };
        Assert.Equal(1, BrainwashPerformanceSchedule.CompletionTick(props));
    }

    [Fact]
    public void CompletionTick_TakesLatestEndOfEverySegment()
    {
        HediffCompProperties_BrainwashPerformance props = new()
        {
            triggerTicks = 60,
            stunDelayTicks = 0,
            stunDurationTicks = 4120,
            sounds =
            [
                new HediffCompProperties_BrainwashPerformance.SoundWithParams { startTick = 60, endTick = 1860 },
                new HediffCompProperties_BrainwashPerformance.SoundWithParams { startTick = 3940, endTick = 4000 },
            ],
            flecks = [new HediffCompProperties_BrainwashPerformance.FleckWithParams { startTick = 60, endTick = 4000 }],
            texts = [new HediffCompProperties_BrainwashPerformance.TextWithParams { startTick = 5000 }],
        };

        Assert.Equal(5000, BrainwashPerformanceSchedule.CompletionTick(props));

        props.texts = null;
        Assert.Equal(4180, BrainwashPerformanceSchedule.CompletionTick(props));
    }

    [Fact]
    public void HeadMoteWindow_CountsOnlyWhenMoteConfigured()
    {
        HediffCompProperties_BrainwashPerformance props = new() { triggerTicks = 10, stunDelayTicks = 0, stunDurationTicks = 10, moteShowEndTick = 999999 };
        Assert.Equal(20, BrainwashPerformanceSchedule.CompletionTick(props));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(120, 120)]
    public void FirstTick_ClampsNegativeStart(int startTick, int expected)
        => Assert.Equal(expected, BrainwashPerformanceSchedule.FirstTick(startTick));

    [Theory]
    [InlineData(100, 60, 160)]
    [InlineData(100, 0, 101)]
    [InlineData(100, -3, 101)]
    public void NextLoopTick_UsesAtLeastOneTickInterval(int age, int loopInterval, int expected)
        => Assert.Equal(expected, BrainwashPerformanceSchedule.NextLoopTick(age, loopInterval));

    [Theory]
    [InlineData(60, 1860, 1860)]
    [InlineData(500, 100, 500)]
    [InlineData(-10, -20, 0)]
    public void EndTick_NeverPrecedesStart(int startTick, int endTick, int expected)
        => Assert.Equal(expected, BrainwashPerformanceSchedule.EndTick(startTick, endTick));

    [Fact]
    public void StunStartTick_IgnoresNegativeDelays()
    {
        Assert.Equal(60, BrainwashPerformanceSchedule.StunStartTick(new HediffCompProperties_BrainwashPerformance { triggerTicks = 60, stunDelayTicks = -30 }));
        Assert.Equal(70, BrainwashPerformanceSchedule.StunStartTick(new HediffCompProperties_BrainwashPerformance { triggerTicks = 60, stunDelayTicks = 10 }));
    }
}
