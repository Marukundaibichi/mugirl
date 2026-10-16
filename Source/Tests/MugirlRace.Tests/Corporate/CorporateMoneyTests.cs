using System.Linq;
using Mugirl.Features.Corporate;

namespace Mugirl.Tests.Corporate;

/// <summary>巨企金额的纯计算：decimal 精确取整、1 亿上限、份额与服务等级，以及与 1.7 float 写法在实际取值范围内的等价性。</summary>
public sealed class CorporateMoneyTests
{
    [Theory]
    [InlineData("0", true, 0)]
    [InlineData("-1", true, 0)]
    [InlineData("0.01", true, 1)]
    [InlineData("0.99", false, 0)]
    [InlineData("12.5", true, 13)]
    [InlineData("12.5", false, 12)]
    [InlineData("100000000", true, 100000000)]
    [InlineData("100000000.01", true, 0)]
    public void Total_RoundsAndRejectsOutOfRange(string total, bool roundUp, int expected)
        => Assert.Equal(expected, CorporateMoney.Total(decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture), roundUp));

    [Fact]
    public void Value_RejectsNonFiniteAndClampsToMaximum()
    {
        Assert.Equal(0m, CorporateMoney.Value(float.NaN));
        Assert.Equal(0m, CorporateMoney.Value(float.PositiveInfinity));
        Assert.Equal(0m, CorporateMoney.Value(-3f));
        Assert.Equal(0m, CorporateMoney.Value(0f));
        Assert.Equal(1.9m, CorporateMoney.Value(1.9f));
        Assert.Equal(100000000m, CorporateMoney.Value(3e9f));
    }

    [Fact]
    public void Price_MatchesDecimalTotalAndRejectsInvalidUnits()
    {
        Assert.Equal(0, CorporateMoney.Price(float.NaN, 3));
        Assert.Equal(0, CorporateMoney.Price(5f, 0));
        Assert.Equal(0, CorporateMoney.Price(2e8f, 1));
        Assert.Equal(20, CorporateMoney.Price(6.5f, 3));
        Assert.Equal(19, CorporateMoney.Price(6.5f, 3, roundUp: false));
        // 1.9f 先转为 7 位有效数字的 decimal 1.9，再乘数量。
        Assert.Equal(57, CorporateMoney.Price(1.9f, 30));
    }

    [Fact]
    public void FloorShare_IsExactPercentage()
    {
        for (int paid = 0; paid <= 200000; paid += 7)
        {
            Assert.Equal((int)((long)paid * 4 / 5), CorporateMoney.FloorShare(paid, 0.8f));
        }
    }

    [Fact]
    public void FloorShare_EqualsFloatMathfOnRealisticAmounts()
    {
        // 1.6/1.7 写法 Mathf.FloorToInt(paid * 0.8f)：乘积小于 2^22 时 float 仍能分辨 0.2 的小数部分，与精确份额相同。
        for (int paid = 0; paid <= 5000000; paid += 997)
        {
            Assert.Equal(UnityMathf.FloorToInt(paid * 0.8f), CorporateMoney.FloorShare(paid, 0.8f));
        }
    }

    [Fact]
    public void FloorShare_StaysExactWhereFloatLosesIntegerPrecision()
    {
        // float(99999999) = 1e8，float 写法得 80000000；精确份额为 79999999.2 向下取整。
        Assert.Equal(80000000, UnityMathf.FloorToInt(99999999 * 0.8f));
        Assert.Equal(79999999, CorporateMoney.FloorShare(99999999, 0.8f));
    }

    [Fact]
    public void Share_HandlesNonFiniteAndHugeFactors()
    {
        Assert.Equal(0, CorporateMoney.CeilingShare(5, float.NaN));
        Assert.Equal(0, CorporateMoney.FloorShare(5, float.NegativeInfinity));
        Assert.Equal(int.MaxValue, CorporateMoney.FloorShare(int.MaxValue, 1e20f));
        Assert.Equal(int.MinValue, CorporateMoney.CeilingShare(int.MaxValue, -1e20f));
        Assert.Equal(-4, CorporateMoney.CeilingShare(-5, 0.8f));
    }

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(9999L, 0)]
    [InlineData(10000L, 1)]
    [InlineData(99999L, 3)]
    [InlineData(400000L, 6)]
    [InlineData(long.MaxValue, 6)]
    public void ServiceLevel_CountsReachedThresholds(long turnover, int expected)
        => Assert.Equal(expected, CorporateMoney.ServiceLevel(turnover, [10000, 25000, 50000, 100000, 200000, 400000]));

    // 客户服务页按等级 1..MaxServiceLevel 读取权益说明键：表必须与阈值等长，且第 n 级对应 Tier{n}（1.6 拼接出的键名）。
    [Fact]
    public void ServiceBenefitKey_CoversEveryServiceLevel()
    {
        for (int level = 1; level <= CorporateNetwork.MaxServiceLevel; level++)
        {
            Assert.Equal("Mugirl.CorporateServices.Tier" + level, CorporateNetwork.ServiceBenefitKey(level));
        }

        Assert.Throws<IndexOutOfRangeException>(() => CorporateNetwork.ServiceBenefitKey(CorporateNetwork.MaxServiceLevel + 1));
    }

    [Theory]
    [InlineData(3, CorporateOrderSpeed.Standard, 180000)]
    [InlineData(3, CorporateOrderSpeed.Express, 90000)]
    [InlineData(3, CorporateOrderSpeed.Priority, 45000)]
    [InlineData(1, CorporateOrderSpeed.Priority, 15000)]
    [InlineData(0, CorporateOrderSpeed.Standard, 0)]
    [InlineData(-2, CorporateOrderSpeed.Express, 0)]
    [InlineData(int.MaxValue, CorporateOrderSpeed.Standard, int.MaxValue)]
    [InlineData(5, (CorporateOrderSpeed)7, 0)]
    public void PreparationTicks_DividesAndSaturates(int days, CorporateOrderSpeed speed, int expected)
        => Assert.Equal(expected, CorporateMoney.PreparationTicks(days, speed, 60000));

    [Fact]
    public void PreparationTicks_RoundsUpToAtLeastOneTick()
        => Assert.Equal(1, CorporateMoney.PreparationTicks(1, CorporateOrderSpeed.Priority, 3));

    [Theory]
    [InlineData(100, CorporateOrderSpeed.Standard, 100)]
    [InlineData(101, CorporateOrderSpeed.Express, 152)]
    [InlineData(101, CorporateOrderSpeed.Priority, 202)]
    [InlineData(0, CorporateOrderSpeed.Express, 0)]
    [InlineData(60000000, CorporateOrderSpeed.Priority, 0)]
    public void ExpeditedTotal_AppliesSpeedFactor(int standard, CorporateOrderSpeed speed, int expected)
        => Assert.Equal(expected, CorporateMoney.ExpeditedTotal(standard, speed));

    [Fact]
    public void AffordableQuantity_MatchesLinearSearch()
    {
        var random = new Random(1729);
        for (int trial = 0; trial < 2000; trial++)
        {
            decimal unit = Math.Round((decimal)(random.NextDouble() * 400.0), 3) + 0.001m;
            int available = random.Next(0, 700);
            int silver = random.Next(0, 60000);
            int expected = 0;
            for (int q = 1; q <= available; q++)
            {
                int quote = CorporateMoney.Total(unit * q);
                if (quote > 0 && quote <= silver)
                {
                    expected = q;
                }
            }

            Assert.Equal(expected, CorporateMoney.AffordableQuantity(unit, available, silver));
        }
    }

    [Fact]
    public void AffordableQuantity_StopsAtTheMoneyCeiling()
    {
        // 超过 1 亿的合计报价为 0（不可购买），可购数量停在上限之下。
        Assert.Equal(1, CorporateMoney.AffordableQuantity(60000000m, 5, int.MaxValue));
        Assert.Equal(0, CorporateMoney.AffordableQuantity(5m, 10, 4));
        Assert.Equal(0, CorporateMoney.AffordableQuantity(5m, -3, 1000));
    }
}
