using Mugirl.Features.Lances;
using Verse;

namespace Mugirl.Tests.Lances;

/// <summary>冲锋冷却与击退方向的纯计算。</summary>
public sealed class LanceChargeRulesTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, -1)]
    [InlineData(-1, 1)]
    [InlineData(-1, -1)]
    public void KnockbackOffset_PushesNeighboursAwayAlongBothAxes(int dx, int dz)
    {
        IntVec3 offset = LanceChargeImpactUtility.KnockbackOffset(new IntVec3(dx, 0, dz), IntVec3.North);
        Assert.Equal(new IntVec3(Math.Sign(dx), 0, Math.Sign(dz)), offset);
    }

    [Fact]
    public void KnockbackOffset_DiagonalNeighbourIsNotTruncatedToFacing()
    {
        // 1.6 把 (1, 0, 1) 单位化后截断成零，退回冲锋者朝向；现在沿斜向推离冲锋格。
        Assert.Equal(new IntVec3(1, 0, 1), LanceChargeImpactUtility.KnockbackOffset(new IntVec3(1, 0, 1), IntVec3.East));
        Assert.Equal(new IntVec3(1, 0, 1), LanceChargeImpactUtility.KnockbackOffset(new IntVec3(2, 0, 1), IntVec3.East));
    }

    [Fact]
    public void KnockbackOffset_SameCellUsesFallbackDirection()
    {
        Assert.Equal(IntVec3.West, LanceChargeImpactUtility.KnockbackOffset(IntVec3.Zero, IntVec3.West));
    }

    [Fact]
    public void Cooldown_TicksLeftNeverNegative()
    {
        Assert.Equal(0, LanceChargeCooldown.TicksLeft(100, 250));
        Assert.Equal(150, LanceChargeCooldown.TicksLeft(400, 250));
    }

    [Fact]
    public void Cooldown_PercentRunsFromZeroToOne()
    {
        Bits.Equal(1f, LanceChargeCooldown.Percent(0, 0, 500), "no cooldown configured");
        Bits.Equal(1f, LanceChargeCooldown.Percent(400, 600, 500), "cooldown already over");
        Bits.Equal(0f, LanceChargeCooldown.Percent(1100, 600, 500), "cooldown just started");
        Bits.Equal(0.5f, LanceChargeCooldown.Percent(800, 600, 500), "half way");
        Bits.Equal(0f, LanceChargeCooldown.Percent(5000, 600, 500), "cooldown longer than configured clamps to zero");
    }
}
