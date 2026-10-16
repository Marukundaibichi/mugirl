using Mugirl.Features.Restraints;

namespace Mugirl.Tests.Restraints;

/// <summary>强行开锁的力量公式与档位要求（Keyed 文本写明：体型 1.0、操作 100%，近战 10 / 14）。</summary>
public sealed class ForceUnlockStrengthTests
{
    [Fact]
    public void Requirements_FollowTheDocumentedBaseline()
    {
        Assert.Equal(6f, ForceUnlockStrength.Required(ForceUnlockRestraintTier.Rough), 4);
        Assert.Equal(7.2f, ForceUnlockStrength.Required(ForceUnlockRestraintTier.Fine), 4);
        Assert.Equal(float.MaxValue, ForceUnlockStrength.Required(ForceUnlockRestraintTier.None));
    }

    [Fact]
    public void PawnExactlyAtBaseline_Passes()
    {
        // 要求值与 Pawn 力量走同一公式，恰好达标的 Pawn 两边逐位相同。
        Assert.True(ForceUnlockStrength.Calculate(1f, 1f, 10) >= ForceUnlockStrength.Required(ForceUnlockRestraintTier.Rough));
        Assert.True(ForceUnlockStrength.Calculate(1f, 1f, 14) >= ForceUnlockStrength.Required(ForceUnlockRestraintTier.Fine));
    }

    [Fact]
    public void OneMeleeLevelShort_Fails()
    {
        Assert.True(ForceUnlockStrength.Calculate(1f, 1f, 9) < ForceUnlockStrength.Required(ForceUnlockRestraintTier.Rough));
        Assert.True(ForceUnlockStrength.Calculate(1f, 1f, 13) < ForceUnlockStrength.Required(ForceUnlockRestraintTier.Fine));
    }

    [Fact]
    public void Calculate_WeightsBodySizeMeleeAndManipulation()
    {
        Bits.Equal((2f * 1f) + (5 * 0.3f) + (0.5f * 2f), ForceUnlockStrength.Calculate(2f, 0.5f, 5));
    }
}
