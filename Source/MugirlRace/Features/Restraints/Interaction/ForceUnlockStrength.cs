namespace Mugirl.Features.Restraints;

// 强行开锁的力量公式：体型 ×1 + 近战等级 ×0.3 + 操作能力 ×2。纯计算，不依赖游戏会话，可离线测试。
// 档位要求与 Keyed 文本 RequirementRough/RequirementFine 一致：体型 1.0、操作 100%，近战 10 / 14。
internal static class ForceUnlockStrength
{
    private const float BodySizeWeight = 1f;
    private const float MeleeSkillWeight = 0.3f;
    private const float ManipulationWeight = 2f;

    private const float RequirementBodySize = 1f;
    private const float RequirementManipulation = 1f;
    private const int RoughRequirementMelee = 10;
    private const int FineRequirementMelee = 14;

    internal static float Calculate(float bodySize, float manipulation, int meleeLevel)
    {
        return (bodySize * BodySizeWeight)
            + (meleeLevel * MeleeSkillWeight)
            + (manipulation * ManipulationWeight);
    }

    // 要求值与 Pawn 力量走同一公式：恰好达到基准的 Pawn 两边结果逐位相同，比较不受浮点舍入影响。
    internal static float Required(ForceUnlockRestraintTier tier)
    {
        return tier switch
        {
            ForceUnlockRestraintTier.Rough => Calculate(RequirementBodySize, RequirementManipulation, RoughRequirementMelee),
            ForceUnlockRestraintTier.Fine => Calculate(RequirementBodySize, RequirementManipulation, FineRequirementMelee),
            _ => float.MaxValue,
        };
    }
}
