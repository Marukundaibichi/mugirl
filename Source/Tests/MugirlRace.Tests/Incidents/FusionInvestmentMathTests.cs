using Mugirl.Features.Incidents;

namespace Mugirl.Tests.Incidents;

/// <summary><see cref="FusionInvestmentMath"/>：牛聚变投资的档位与奶制食品奖励数量。</summary>
public sealed class FusionInvestmentMathTests
{
    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(999, 1)]
    [InlineData(1000, 1)]
    [InlineData(2999, 1)]
    [InlineData(3000, 2)]
    [InlineData(4999, 2)]
    [InlineData(5000, 3)]
    [InlineData(1_000_000, 3)]
    public void TierForAmount_PicksHighestReachedTier(int amount, int expected)
        => Assert.Equal(expected, FusionInvestmentMath.TierForAmount(amount));

    [Theory]
    [InlineData(int.MinValue, 30)]
    [InlineData(0, 30)]
    [InlineData(1, 30)]
    [InlineData(2, 90)]
    [InlineData(3, 180)]
    [InlineData(4, 180)]
    [InlineData(int.MaxValue, 180)]
    public void MilkFoodRewardCountForTier_ClampsToKnownTiers(int tier, int expected)
        => Assert.Equal(expected, FusionInvestmentMath.MilkFoodRewardCountForTier(tier));

    [Fact]
    public void Tables_MatchTheThreeDialogOptions()
    {
        Assert.Equal(new[] { 1000, 3000, 5000 }, FusionInvestmentMath.InvestmentAmounts.ToArray());
        Assert.Equal(FusionInvestmentMath.InvestmentAmounts.Length, FusionInvestmentMath.MilkFoodRewardCounts.Length);
    }
}
