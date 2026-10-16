using Mugirl.Compatibility.YayoCombat;

namespace Mugirl.Tests.Compatibility;

/// <summary>
/// Yayo's Combat 生成武器的初始弹药：保留 1.6 <c>Mathf.RoundToInt</c> 的 Mono 规则（NaN/越界 → int.MinValue → 0 发），
/// 不能变成 1.7 饱和转换的 int.MaxValue 发。
/// </summary>
public sealed class YayoCombatChargesTests
{
    [Theory]
    [InlineData(10, 1f, 1f, true, 10)]
    [InlineData(10, 0.5f, 1f, true, 5)]
    [InlineData(10, 2f, 1.3f, false, 26)]
    [InlineData(10, 2f, 1.3f, true, 10)]
    [InlineData(1, 2.5f, 1f, false, 2)]
    [InlineData(1, 3.5f, 1f, false, 4)]
    [InlineData(10, -1f, 1f, true, 0)]
    [InlineData(0, 5f, 1.3f, false, 0)]
    public void FollowsTheRoundedFormula(int maxCharges, float factor, float random, bool cap, int expected)
        => Assert.Equal(expected, YayoCombatCompatibility.GeneratedCharges(maxCharges, factor, random, cap));

    [Theory]
    [InlineData(float.NaN, false)]
    [InlineData(float.NaN, true)]
    [InlineData(float.PositiveInfinity, false)]
    [InlineData(1e30f, false)]
    [InlineData(float.NegativeInfinity, true)]
    public void NonFiniteOrOverflowingFactorsGiveNoAmmo(float factor, bool cap)
        => Assert.Equal(0, YayoCombatCompatibility.GeneratedCharges(10, factor, 1f, cap));

    [Fact]
    public void RoundingMatchesUnityStubsRoundToInt()
    {
        var random = new Random(17);
        for (int i = 0; i < 20000; i++)
        {
            int max = random.Next(0, 400);
            float factor = (float)(random.NextDouble() * 20.0);
            float roll = 0.7f + (float)(random.NextDouble() * 0.6);
            int expected = Math.Max(0, UMathf.RoundToInt(max * factor * roll));
            Assert.Equal(expected, YayoCombatCompatibility.GeneratedCharges(max, factor, roll, capAtMaxCharges: false));
        }
    }
}
