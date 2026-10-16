using Mugirl.Features.Corporate;

namespace Mugirl.Tests.Corporate;

/// <summary><see cref="CorporateRaidTierRules"/>：巨企战斗编组按袭击点数分档，边界包含上限、不含下一档的下限。</summary>
public sealed class CorporateRaidTierRulesTests
{
    [Theory]
    [InlineData(2000f, 0f, true)]
    [InlineData(2000f, 2000f, true)]
    [InlineData(2000f, 2000.001f, false)]
    [InlineData(2000f, -1f, false)]
    [InlineData(4000f, 2000f, false)]
    [InlineData(4000f, 2000.001f, true)]
    [InlineData(4000f, 4000f, true)]
    [InlineData(6500f, 4000f, false)]
    [InlineData(6500f, 6500f, true)]
    [InlineData(10000f, 6500f, false)]
    [InlineData(10000f, 10000f, true)]
    [InlineData(10000f, 10000.5f, false)]
    public void TierGroups_AcceptOnlyTheirPointRange(float maxTotalPoints, float points, bool expected)
    {
        Assert.True(CorporateRaidTierRules.TryGetTierAllowance(maxTotalPoints, points, out bool allowed));
        Assert.Equal(expected, allowed);
    }

    [Theory]
    [InlineData(1500f)]
    [InlineData(99999f)]
    [InlineData(float.NaN)]
    public void OtherGroups_KeepVanillaResult(float maxTotalPoints)
        => Assert.False(CorporateRaidTierRules.TryGetTierAllowance(maxTotalPoints, 3000f, out _));

    [Fact]
    public void NaNPoints_AreNeverAllowed()
    {
        Assert.True(CorporateRaidTierRules.TryGetTierAllowance(2000f, float.NaN, out bool allowed));
        Assert.False(allowed);
    }
}
