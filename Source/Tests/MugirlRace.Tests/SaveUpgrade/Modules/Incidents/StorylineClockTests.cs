using Mugirl.Features.Incidents;

namespace Mugirl.Tests.SaveUpgrade.Modules.Incidents;

public sealed class StorylineClockTests
{
    [Fact]
    public void FirstActiveTickConsumesOneAndDoesNotAdvanceBeforeScheduledWake()
    {
        var clock = new StorylineClock();
        Assert.True(clock.TryBegin(125000, true, out int elapsed, out bool stale));
        Assert.Equal(1, elapsed);
        Assert.True(stale);
        Assert.Equal(125250, clock.nextStaleCheckTick);
        clock.nextTick = 125137;
        Assert.False(clock.TryBegin(125136, true, out _, out _));
        Assert.Equal(125000, clock.lastTick);
        Assert.True(clock.TryBegin(125137, true, out elapsed, out stale));
        Assert.Equal(137, elapsed);
        Assert.False(stale);
    }

    [Fact]
    public void ClockMatchesPreRefactorOrderForWakeupsRepeatedTicksAndOverflowBoundary()
    {
        var random = new Random(1931);
        var clock = new StorylineClock();
        int last = -1, next = -1, staleTick = -1;
        for (int i = 0; i < 10000; i++)
        {
            int tick = i == 9999 ? int.MaxValue - 100 : random.Next(0, 900000);
            bool active = random.Next(2) == 1;
            if (i % 7 == 0)
            {
                next = clock.nextTick = -1;
            }

            if (last < 0) { last = tick - 1; }
            if (next < 0) { next = tick; }
            if (staleTick < 0) { staleTick = tick; }
            bool due = next <= tick;
            int elapsed = 0;
            bool check = false;
            if (due)
            {
                elapsed = tick - last;
                if (elapsed <= 0) { elapsed = 1; }
                last = tick;
                if (active && staleTick <= tick)
                {
                    staleTick = StorylineSchedule.SafeAddTicks(tick, 250);
                    check = true;
                }
            }

            Assert.Equal(due, clock.TryBegin(tick, active, out int actualElapsed, out bool actualCheck));
            Assert.Equal(elapsed, actualElapsed);
            Assert.Equal(check, actualCheck);
            Assert.Equal(last, clock.lastTick);
            Assert.Equal(next, clock.nextTick);
            Assert.Equal(staleTick, clock.nextStaleCheckTick);
            if (due) { next = clock.nextTick = StorylineSchedule.NextWakeTick(tick, random.Next(1, 800)); }
        }
    }
}
