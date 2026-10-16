using Mugirl.Mathematics;
using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade.Modules.Corporate;

public sealed class CorporateLegacyDefaultsTests
{
    [Fact]
    public void LegacySalesTarget_IsCeilingOfEightyPercentAndAtLeastOne()
    {
        for (int count = -20; count <= 100000; count++)
        {
            int exact = count <= 0 ? 1 : Math.Max(1, (int)(((long)count * 4 + 4) / 5));
            Assert.Equal(exact, CorporateLegacyDefaults.DistributionTarget(count));
        }
    }

    [Fact]
    public void LegacySalesTarget_EqualsTheFloatFormulaOnTheContractRange()
    {
        // 分销合同数量被钳制在 10–300；1.7 的 Mathf.CeilToInt(count * 0.8f) 在此范围内与精确结果一致。
        for (int count = 1; count <= 20000; count++)
        {
            Assert.Equal(Math.Max(1, UnityMathf.CeilToInt(count * 0.8f)), CorporateLegacyDefaults.DistributionTarget(count));
        }
    }

}
