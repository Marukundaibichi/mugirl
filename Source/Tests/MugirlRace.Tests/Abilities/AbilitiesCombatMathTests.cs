using System.Collections.Generic;
using Mugirl.Features.Abilities;
using Mugirl.Features.AdvancedArmor;
using Verse;

namespace Mugirl.Tests.Abilities;

/// <summary>投掷数值、冲锋击退方向、走线与神经肩甲耐久损耗。</summary>
public sealed class AbilitiesCombatMathTests
{
    [Theory]
    [InlineData(0f, 1f, 0f)]
    [InlineData(10f, 20f, 0f)]
    [InlineData(10f, 10f, 12f)]
    [InlineData(40f, 10f, 18f)]
    public void MaxThrowDistance_FollowsStrengthToMassRatio(float strength, float mass, float expected)
        => Bits.Equal(expected, ThrowBalance.MaxThrowDistance(strength, mass));

    [Fact]
    public void MaxThrowDistance_IsCappedByRatioTen()
    {
        float capped = ThrowBalance.MaxThrowDistance(100f, 10f);
        Bits.Equal(capped, ThrowBalance.MaxThrowDistance(1e6f, 1f));
        Bits.Equal(6f + 6f * MathF.Sqrt(10f), capped);
    }

    [Theory]
    [InlineData(0.1f, 0f, 6)]
    [InlineData(100f, 10f, 46)] // 4 + 35 + 7.5 = 46.5：与 Unity 相同的银行家舍入
    [InlineData(1e6f, 1e6f, 110)]
    public void ImpactDamage_RoundsLikeUnityAndClamps(float mass, float distance, int expected)
        => Assert.Equal(expected, ThrowBalance.ImpactDamage(mass, distance));

    [Theory]
    [InlineData(0, 2f)]
    [InlineData(1000, 6f)]
    public void ExplosionRadius_IsClamped(int damage, float expected)
        => Bits.Equal(expected, ThrowBalance.ExplosionRadius(damage));

    [Fact]
    public void DunkImpact_AmplifiesEighteenCellThrow()
    {
        Assert.Equal(UnityMathf.RoundToInt(ThrowBalance.ImpactDamage(60f, 18f) * 1.7f), ThrowBalance.DunkImpactDamage(60f));
    }

    [Fact]
    public void UnweightedBuilding_UsesLargestOfDurabilityFootprintAndMaterial()
    {
        Bits.Equal(3000f, ThrowBalance.UnweightedBuildingMass(5f, maxHitPoints: 300, sizeX: 1, sizeZ: 1, costStuffCount: 5));
        Bits.Equal(400f, ThrowBalance.UnweightedBuildingMass(5f, maxHitPoints: 10, sizeX: 4, sizeZ: 4, costStuffCount: -3));
        Bits.Equal(5000f, ThrowBalance.UnweightedBuildingMass(5000f, maxHitPoints: 10, sizeX: 1, sizeZ: 1, costStuffCount: 0));
        Bits.Equal(900f, ThrowBalance.MineableMass(1f, 90));
    }

    [Theory]
    [InlineData(1, 0, 6, 6, 0)]
    [InlineData(0, -2, 6, 0, -6)]
    [InlineData(1, 1, 1, 1, 1)] // 1.6 把 (0.707, 0.707) 截断成 (0, 0)，斜向击退失效
    [InlineData(-2, 2, 6, -4, 4)]
    [InlineData(3, 1, 6, 6, 2)]
    [InlineData(0, 0, 6, 0, 0)]
    public void KnockbackOffset_RoundsDirectionTimesDistance(int dx, int dz, int distance, int expectedX, int expectedZ)
    {
        IntVec3 from = new(10, 0, 10);
        IntVec3 offset = ChargeKnockbackUtility.KnockbackOffset(from, from + new IntVec3(dx, 0, dz), distance);
        Assert.Equal(new IntVec3(expectedX, 0, expectedZ), offset);
    }

    [Fact]
    public void LineCells_MatchVanillaPointsOnLineOfSight()
    {
        Random random = new(20261014);
        List<IntVec3> cells = [];
        for (int i = 0; i < 5000; i++)
        {
            IntVec3 start = new(random.Next(-40, 40), 0, random.Next(-40, 40));
            IntVec3 end = new(random.Next(-40, 40), 0, random.Next(-40, 40));
            GridLineUtility.LineCellsNonAlloc(start, end, cells);
            Assert.Equal(GenSight.PointsOnLineOfSight(start, end), cells);
        }
    }

    [Theory]
    [InlineData(100f, 4)]
    [InlineData(1000f, 8)]
    [InlineData(1f, 1)]
    [InlineData(0f, 1)]
    [InlineData(float.PositiveInfinity, 1)] // Unity CeilToInt 得 int.MinValue，钳到 1（饱和转换会得到上限 8）
    [InlineData(float.NaN, 1)]
    public void ShoulderDeflection_DurabilityLossKeepsUnityRounding(float damage, int expected)
        => Assert.Equal(expected, CompNeuralShoulderDeflection.DurabilityDamage(damage, 0.04f, 8));

    [Fact]
    public void ShoulderDeflection_NonPositiveMaximumStillCostsOne()
        => Assert.Equal(1, CompNeuralShoulderDeflection.DurabilityDamage(500f, 0.04f, 0));
}
