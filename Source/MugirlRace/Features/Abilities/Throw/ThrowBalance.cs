namespace Mugirl.Features.Abilities;

// 投掷数值公式（纯函数，不读游戏状态）。数值改动须同步记录原因与影响。
internal static class ThrowBalance
{
    private const float BuildingMassPerCell = 25f;
    private const float BuildingCostMassFactor = 0.5f;
    private const float UnweightedBuildingMassPerHitPoint = 10f;

    /// <summary>最远投掷距离（格）。力量不足以举起、或质量/力量无效时为 0。</summary>
    internal static float MaxThrowDistance(float strength, float mass)
    {
        if (strength <= 0f || mass <= 0f || mass > strength)
        {
            return 0f;
        }

        float ratio = UnityMathf.Clamp(strength / mass, 1f, 10f);
        return UnityMathf.Clamp(6f + 6f * UnityMathf.Sqrt(ratio), 8f, 25f);
    }

    /// <summary>落点冲击伤害。2026-09-26 削减：各系数与上下限减半（原 8 + √质量×7 + 距离×1.5，范围 12..220）。</summary>
    internal static int ImpactDamage(float mass, float distance)
    {
        return UnityMathf.RoundToInt(UnityMathf.Clamp(4f + UnityMathf.Sqrt(UnityMathf.Max(mass, 0.1f)) * 3.5f + distance * 0.75f, 6f, 110f));
    }

    /// <summary>落点爆炸半径（格）。</summary>
    internal static float ExplosionRadius(int damage)
    {
        return UnityMathf.Clamp(1.7f + damage / 45f, 2f, 6f);
    }

    /// <summary>扣篮触地伤害：按 18 格投掷的冲击伤害放大 1.7 倍。</summary>
    internal static int DunkImpactDamage(float mass)
    {
        return UnityMathf.RoundToInt(ImpactDamage(mass, 18f) * 1.7f);
    }

    /// <summary>
    /// 质量下限：无 Mass 属性的建筑按完整耐久度、占地与材料三者的较大值赋予吨级重量；显式 Mass 较高时保留原值。
    /// </summary>
    internal static float UnweightedBuildingMass(float mass, int maxHitPoints, int sizeX, int sizeZ, int costStuffCount)
    {
        float durabilityMass = maxHitPoints * UnweightedBuildingMassPerHitPoint;
        float footprintMass = sizeX * sizeZ * BuildingMassPerCell;
        float materialMass = Math.Max(costStuffCount, 0) * BuildingCostMassFactor;
        return UnityMathf.Max(mass, UnityMathf.Max(durabilityMass, UnityMathf.Max(footprintMass, materialMass)));
    }

    /// <summary>山体的质量下限：完整耐久度形成的重量；显式 Mass 较高时保留原值。</summary>
    internal static float MineableMass(float mass, int maxHitPoints)
    {
        return UnityMathf.Max(mass, maxHitPoints * UnweightedBuildingMassPerHitPoint);
    }
}
