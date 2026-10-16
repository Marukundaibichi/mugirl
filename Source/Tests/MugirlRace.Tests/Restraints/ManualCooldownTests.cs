using System.Globalization;
using Mugirl.Features.Restraints;

namespace Mugirl.Tests.Restraints;

/// <summary>高级束具手动按钮冷却：与 1.6 的 Mathf 写法逐位一致，冷却时长至少 1 tick。</summary>
public sealed class ManualCooldownTests
{
    [Theory]
    [InlineData(-99999, 480, 0, 0)] // 新项圈的默认上次使用时间：立即可用。
    [InlineData(-999999, 600, 0, 0)]
    [InlineData(1000, 480, 1200, 280)]
    [InlineData(1000, 480, 1480, 0)]
    [InlineData(1000, 480, 1479, 1)]
    [InlineData(100, 0, 100, 1)] // 冷却配置为 0 时按 1 tick 处理。
    [InlineData(100, -5, 101, 0)]
    public void TicksLeft_MatchesExpected(int lastUseTick, int cooldownTicks, int currentTick, int expected)
        => Assert.Equal(expected, ManualCooldown.TicksLeft(lastUseTick, cooldownTicks, currentTick));

    [Fact]
    public void Percent_IsOneWhenReady_ZeroWhenJustUsed()
    {
        Bits.Equal(1f, ManualCooldown.Percent(0, 480));
        Bits.Equal(1f, ManualCooldown.Percent(-3, 480));
        Bits.Equal(0f, ManualCooldown.Percent(480, 480));
        Bits.Equal(0f, ManualCooldown.Percent(5, 0), "cooldown 0 is treated as 1 tick");
    }

    [Fact]
    public void MatchesLegacyMathfFormula()
    {
        Random random = new(20261014);
        Mismatches mismatches = new("ManualCooldown vs 1.6 Mathf formula");
        for (int i = 0; i < Samples.RandomCount; i++)
        {
            int last = random.Next(-1_000_000, 5_000_000);
            int cooldown = random.Next(-10, 70_000);
            int current = last + random.Next(-100, 80_000);

            int legacyLeft = UMathf.Max(0, (last + UMathf.Max(1, cooldown)) - current);
            float legacyPercent = legacyLeft <= 0 ? 1f : UMathf.Clamp01(1f - (float)legacyLeft / UMathf.Max(1, cooldown));

            int left = ManualCooldown.TicksLeft(last, cooldown, current);
            string Describe() => string.Create(CultureInfo.InvariantCulture, $"last={last} cooldown={cooldown} current={current}");
            mismatches.Check(legacyLeft, left, Describe);
            mismatches.Check(legacyPercent, ManualCooldown.Percent(left, cooldown), Describe);
        }

        mismatches.AssertNone();
    }
}
