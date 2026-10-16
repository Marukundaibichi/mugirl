using Mugirl.Features.Appearance;

namespace Mugirl.Tests.Appearance;

/// <summary>身体附件变体：Lovin 印记掷骰区间与持续时间配置的归一化。</summary>
public sealed class BodyAccessoryVariantsTests
{
    [Theory]
    [InlineData(0f, BodyAccessoryVariants.LovinBite)]
    [InlineData(0.2499f, BodyAccessoryVariants.LovinBite)]
    [InlineData(0.25f, BodyAccessoryVariants.LovinHandprint)]
    [InlineData(0.4999f, BodyAccessoryVariants.LovinHandprint)]
    [InlineData(0.5f, BodyAccessoryVariants.None)]
    [InlineData(0.9999f, BodyAccessoryVariants.None)]
    public void SelectLovinMark_SplitsTheRollIntoHalfOpenRanges(float roll, int expected)
    {
        Assert.Equal(expected, BodyAccessoryVariants.SelectLovinMark(roll, 0.25f, 0.25f));
    }

    [Fact]
    public void SelectLovinMark_ZeroChancesNeverMark()
    {
        Assert.Equal(BodyAccessoryVariants.None, BodyAccessoryVariants.SelectLovinMark(0f, 0f, 0f));
    }

    [Theory]
    [InlineData(60000, 180000, 60000, 180000)]
    [InlineData(0, -5, 1, 1)]
    [InlineData(100, 50, 100, 100)]
    public void NormalizeMarkDuration_KeepsAPositiveOrderedRange(int min, int max, int expectedMin, int expectedMax)
    {
        Assert.Equal((expectedMin, expectedMax), BodyAccessoryVariants.NormalizeMarkDuration(min, max));
    }

    [Fact]
    public void IsLovinMark_AcceptsOnlyTemporaryMarks()
    {
        Assert.True(BodyAccessoryVariants.IsLovinMark(BodyAccessoryVariants.LovinBite));
        Assert.True(BodyAccessoryVariants.IsLovinMark(BodyAccessoryVariants.LovinHandprint));
        Assert.False(BodyAccessoryVariants.IsLovinMark(BodyAccessoryVariants.None));
        Assert.False(BodyAccessoryVariants.IsLovinMark(BodyAccessoryVariants.Barcode));
        Assert.False(BodyAccessoryVariants.IsLovinMark(BodyAccessoryVariants.ChemicalScar));
    }
}
