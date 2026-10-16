using Mugirl.Features.Abilities;

namespace Mugirl.Tests.Abilities;

/// <summary>投掷物与扣篮道具的绘制补间、放置退避与 tick 周期动画（纯逻辑，不需要游戏会话）。</summary>
public sealed class AbilitiesPresentationTests
{
    [Fact]
    public void Interpolator_SamplesOncePerFrame()
    {
        RenderTickInterpolator interpolator = default;
        Assert.True(interpolator.BeginFrame(10, simulatedTicks: 0));
        Assert.False(interpolator.BeginFrame(10, simulatedTicks: 0), "the same engine frame is sampled once");
        Assert.True(interpolator.BeginFrame(11, simulatedTicks: 0));
    }

    [Fact]
    public void Interpolator_FirstSampleCatchesUpToPreviousSimulatedTick()
    {
        RenderTickInterpolator fresh = default;
        fresh.BeginFrame(5, simulatedTicks: 1);
        Bits.Equal(0f, fresh.Ticks, "one simulated tick does not trigger a catch-up");

        RenderTickInterpolator loaded = default;
        loaded.BeginFrame(5, simulatedTicks: 12);
        Bits.Equal(11f, loaded.Ticks, "a first sample after loading starts one tick behind the simulation");
    }

    [Fact]
    public void Interpolator_SkippedFramesCatchUp()
    {
        RenderTickInterpolator interpolator = default;
        interpolator.BeginFrame(1, simulatedTicks: 0);
        interpolator.BeginFrame(2, simulatedTicks: 6);
        Bits.Equal(0f, interpolator.Ticks, "consecutive frames advance only through Advance");

        interpolator.BeginFrame(4, simulatedTicks: 6);
        Bits.Equal(5f, interpolator.Ticks, "a skipped frame (off-screen) jumps to the previous simulated tick");
    }

    [Fact]
    public void Interpolator_AdvanceIsClampedToCeiling()
    {
        RenderTickInterpolator interpolator = default;
        interpolator.Advance(frameSeconds: 1f / 60f, tickRateMultiplier: 3f, ceiling: 10f);
        Bits.Equal(3f, interpolator.Ticks);

        interpolator.Advance(frameSeconds: 5f, tickRateMultiplier: 150f, ceiling: 10f);
        Bits.Equal(10f, interpolator.Ticks, "a hitch frame never draws past the simulated tick");

        interpolator.Advance(frameSeconds: -1f, tickRateMultiplier: -2f, ceiling: 10f);
        Bits.Equal(10f, interpolator.Ticks, "negative time or rate does not rewind");

        interpolator.Reset(4f);
        Bits.Equal(4f, interpolator.Ticks);
        Assert.True(interpolator.BeginFrame(4, simulatedTicks: 4), "Reset forgets the last sampled frame");
    }

    [Fact]
    public void Backoff_DoublesUpToMaximum()
    {
        DropRetryBackoff backoff = default;
        Assert.False(backoff.IsWaiting(0));

        int[] expected = [30, 60, 120, 120];
        int now = 1000;
        foreach (int delay in expected)
        {
            backoff.Schedule(now);
            Assert.Equal(delay, backoff.DelayTicks);
            Assert.True(backoff.IsWaiting(now + delay - 1));
            Assert.False(backoff.IsWaiting(now + delay));
            now += delay;
        }
    }

    [Fact]
    public void WrappedDegrees_MatchesFloatProductWhileExact_AndStaysInRangeLater()
    {
        for (int tick = 0; tick < 200_000; tick++)
        {
            Bits.Equal((tick * 24f) % 360f, DunkPresentationMath.DribbleSpinDegrees(tick), $"tick {tick}");
        }

        int[] late = [16_777_216, 36_000_007, int.MaxValue, -7];
        foreach (int tick in late)
        {
            float degrees = DunkPresentationMath.DribbleSpinDegrees(tick);
            Assert.InRange(degrees, 0f, 336f);
            Assert.Equal(0f, degrees % 24f);
        }

        Bits.Equal(192f, TickAnimation.WrappedDegrees(-7, 15, 24f), "negative ticks wrap forward");
    }

    [Fact]
    public void TickSin_UsesDoublePrecisionProduct()
    {
        Bits.Equal((float)Math.Sin(36_000_007 * (double)0.21f), TickAnimation.Sin(36_000_007, 0.21f));
        Bits.Equal(0f, TickAnimation.Sin(0, 0.21f));
    }
}
