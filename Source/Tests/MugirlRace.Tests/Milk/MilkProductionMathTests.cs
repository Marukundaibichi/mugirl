using Mugirl.Features.Milk;

namespace Mugirl.Tests.Milk;

/// <summary>产奶累积、乳房倍率优先级与分堆数量。</summary>
public sealed class MilkProductionMathTests
{
    [Fact]
    public void FullnessPerTick_OneDayFullSlot_FillsInOneDay()
    {
        float perTick = MilkProductionMath.FullnessPerTick(100f, 1f, 1f);
        Bits.Equal(1f / 60000f, perTick, "100% per 1 day");
        Assert.Equal(1f, MilkProductionMath.Accrue(0f, perTick, 60000), 4);
    }

    [Fact]
    public void FullnessPerTick_MatchesLegacyExpressionOrder()
    {
        // 1.6：targetFullnessGain * multiplier / intervalTicks，其中 intervalTicks = Max(1, days * 60000)、gain = Max(0, amount) / 100。
        float amount = 37.5f;
        float days = 1.7f;
        float multiplier = 1.25f;
        float expected = UnityMathf.Max(0f, amount) / 100f * multiplier / UnityMathf.Max(1f, days * 60000f);
        Bits.Equal(expected, MilkProductionMath.FullnessPerTick(amount, days, multiplier));
    }

    [Fact]
    public void FullnessPerTick_ClampsNegativeInputsAndZeroInterval()
    {
        Bits.Equal(0f, MilkProductionMath.FullnessPerTick(-5f, 1f, 1f), "negative amount");
        Bits.Equal(0f, MilkProductionMath.FullnessPerTick(10f, 1f, -2f), "negative multiplier");
        Bits.Equal(0.5f, MilkProductionMath.FullnessPerTick(50f, 0f, 1f), "zero interval uses one tick");
    }

    [Fact]
    public void Accrue_CapsAtOne()
    {
        Bits.Equal(1f, MilkProductionMath.Accrue(0.99f, 0.01f, 60));
        Assert.Equal(0.5f, MilkProductionMath.Accrue(0.25f, 0.25f / 60f, 60), 5);
    }

    [Fact]
    public void BreastMultiplier_FollowsLegacyPriority()
    {
        Assert.False(MilkProductionMath.TryGetBreastYieldMultiplier(new BreastProfile(), out float none));
        Bits.Equal(0f, none);

        AssertMultiplier(new BreastProfile { FlatOrMale = true }, 0.5f);
        AssertMultiplier(new BreastProfile { Large = true, FlatOrMale = true }, 1.25f);
        AssertMultiplier(new BreastProfile { Small = true, Large = true }, 0.75f);
        AssertMultiplier(new BreastProfile { Normal = true, Small = true }, 1f);
        AssertMultiplier(new BreastProfile { Special = true, Normal = true, FlatOrMale = true }, 1.5f);
    }

    [Theory]
    [InlineData(1, 75, 1)]
    [InlineData(75, 75, 75)]
    [InlineData(200, 75, 75)]
    [InlineData(5, 0, 1)]
    [InlineData(5, -3, 1)]
    public void NextStackSize_RespectsStackLimit(int amount, int stackLimit, int expected)
    {
        Assert.Equal(expected, MilkOutputUtility.NextStackSize(amount, stackLimit));
    }

    private static void AssertMultiplier(BreastProfile profile, float expected)
    {
        Assert.True(MilkProductionMath.TryGetBreastYieldMultiplier(profile, out float multiplier));
        Bits.Equal(expected, multiplier);
    }
}
