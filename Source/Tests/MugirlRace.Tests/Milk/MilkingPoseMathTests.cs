using Mugirl.Features.Milk;

namespace Mugirl.Tests.Milk;

/// <summary>挤奶/喂奶动画的纯几何：脉冲、抖动噪声、侧倾、方向与喂奶双人姿态求解。</summary>
public sealed class MilkingPoseMathTests
{
    private const float Tolerance = 1e-4f;

    [Fact]
    public void NoiseSigned_IsDeterministicAndInRange()
    {
        Random random = new(17);
        for (int i = 0; i < 20000; i++)
        {
            int seed = random.Next(int.MinValue, int.MaxValue);
            float value = MilkingPoseMath.NoiseSigned(seed);
            Assert.InRange(value, -1f, 1.0001f);
            Bits.Equal(value, MilkingPoseMath.NoiseSigned(seed));
        }
    }

    [Fact]
    public void PulseEnvelope_IsHalfSineInsideDuration()
    {
        Bits.Equal(0f, MilkingPoseMath.PulseEnvelope(-1));
        Bits.Equal(0f, MilkingPoseMath.PulseEnvelope(MilkingPoseMath.PulseDurationTicks));
        Bits.Equal(0f, MilkingPoseMath.PulseEnvelope(0));
        Assert.Equal(1f, MilkingPoseMath.PulseEnvelope(MilkingPoseMath.PulseDurationTicks / 2), 5);
    }

    [Fact]
    public void AddPulseTransform_OutsideDurationLeavesTransformUnchanged()
    {
        Vector3 offset = new(1f, 2f, 3f);
        Vector3 scale = Vector3.One;
        MilkingPoseMath.AddPulseTransform(MilkingPoseMath.PulseDurationTicks, 123, 1f, ref offset, ref scale);
        Assert.Equal(new Vector3(1f, 2f, 3f), offset);
        Assert.Equal(Vector3.One, scale);
    }

    [Fact]
    public void AddPulseTransform_JitterStaysFlatAndBounded()
    {
        for (int age = 0; age < MilkingPoseMath.PulseDurationTicks; age++)
        {
            Vector3 offset = Vector3.Zero;
            Vector3 scale = Vector3.One;
            MilkingPoseMath.AddPulseTransform(age, 98765, 1f, ref offset, ref scale);
            Bits.Equal(0f, offset.Y, "jitter is horizontal");
            Assert.True(UnityVector.Magnitude(offset) <= 0.045f + Tolerance, "jitter amplitude");
            Bits.Equal(1f, scale.Y);
        }
    }

    [Fact]
    public void LeanAngle_LeansAwayFromPartnerWithinLimit()
    {
        Bits.Equal(0f, MilkingPoseMath.LeanAngleForDirection(Vector3.Zero, 7f));
        Bits.Equal(-7f, MilkingPoseMath.LeanAngleForDirection(Vector3.UnitX, 7f));
        Bits.Equal(7f, MilkingPoseMath.LeanAngleForDirection(UnityVector.Left, 7f));
        Bits.Equal(0f, MilkingPoseMath.LeanAngleForDirection(Vector3.UnitZ, 7f));
    }

    [Fact]
    public void HorizontalDirection_IgnoresHeightAndCollapsesNearPoints()
    {
        Vector3 direction = MilkingPoseMath.HorizontalDirection(new Vector3(1f, 5f, 1f), new Vector3(4f, -2f, 5f));
        Bits.Equal(0f, direction.Y);
        Assert.Equal(1f, UnityVector.Magnitude(direction), 5);
        Assert.Equal(Vector3.Zero, MilkingPoseMath.HorizontalDirection(new Vector3(1f, 0f, 1f), new Vector3(1.005f, 3f, 1f)));
    }

    [Theory]
    [InlineData(1f, 0f, 7, 1f)]
    [InlineData(-1f, 0f, 7, -1f)]
    [InlineData(0.01f, 1f, 7, 1f)]
    [InlineData(0.01f, -1f, 7, -1f)]
    [InlineData(0f, 0f, 8, 1f)]
    [InlineData(0f, 0f, 7, -1f)]
    public void InteractionSideSign_PrefersEastWestThenNorthSouthThenId(float dx, float dz, int id, float expected)
    {
        Bits.Equal(expected, MilkingPoseMath.InteractionSideSign(new Vector3(10f, 0f, 10f), new Vector3(10f + dx, 0f, 10f + dz), id));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void SolvePairPose_PlacesRotatedHeadOnSourceChest(float sideSign)
    {
        PawnVisualMetrics source = new(new Vector2(1.5f, 1.5f), new Vector2(0.6f, 0.6f), new Vector3(0f, 0f, 0.34f), true);
        PawnVisualMetrics child = new(new Vector2(0.9f, 0.9f), new Vector2(0.5f, 0.5f), new Vector3(0.02f, 0f, 0.2f), true);
        Vector3 sourcePos = new(10.5f, 3f, 20.5f);
        Vector3 childPos = sourcePos + new Vector3(sideSign, 0f, 0f);

        InteractionPairPose pose = MilkingPoseMath.SolvePairPose(source, child, sourcePos, childPos, sideSign, default);

        // 被喂者按体型横向倾倒（62°–82°），方向与所在侧相反。
        float tilt = -sideSign * pose.Recipient.RootWorldAngle;
        Assert.InRange(tilt, 62f, 82f);

        // 旋转后的头部锚点落在雪牛娘胸前：横向在被喂者对侧 0.09–0.26 格之间，纵向为身高的 -10%（钳制到 [-0.18, -0.04]），高度抬 0.012。
        Vector3 head = childPos + pose.Recipient.RootWorldOffset + UnityVector.RotatedBy(child.HeadAnchor, pose.Recipient.RootWorldAngle);
        Vector3 chest = head - (sourcePos + pose.Source.RootWorldOffset);
        Assert.Equal(-sideSign * 0.24f, chest.X, 4);
        Assert.Equal(0.012f, chest.Y, 4);
        Assert.Equal(-0.15f, chest.Z, 4);

        // 雪牛娘略靠向被喂者 0.11 格，不旋转。
        Assert.Equal(sideSign * 0.11f, pose.Source.RootWorldOffset.X, 5);
        Bits.Equal(0f, MathF.Abs(pose.Source.RootWorldAngle));
        Assert.True(pose.Source.HasExplicitHead);
        Bits.Equal(1.5f, pose.Source.BodyHeight);
        Bits.Equal(0.9f, pose.Recipient.BodyHeight);
    }

    [Fact]
    public void SolvePairPose_AppliesMirroredLayoutOffsets()
    {
        PawnVisualMetrics metrics = MilkingPoseMath.FallbackMetrics();
        Vector3 sourcePos = new(0f, 0f, 0f);
        Vector3 recipientPos = new(-1f, 0f, 0f);
        InteractionPairPose baseline = MilkingPoseMath.SolvePairPose(metrics, metrics, sourcePos, recipientPos, -1f, default);
        MilkFeedingLayoutEntry layout = new() { SourceX = 0.1f, SourceZ = 0.05f, RecipientX = -0.2f, RecipientZ = 0.3f };
        InteractionPairPose tuned = MilkingPoseMath.SolvePairPose(metrics, metrics, sourcePos, recipientPos, -1f, layout);

        Vector3 sourceDelta = tuned.Source.RootWorldOffset - baseline.Source.RootWorldOffset;
        Assert.Equal(-0.1f, sourceDelta.X, 5);
        Assert.Equal(0.05f, sourceDelta.Z, 5);

        // 被喂者的位移包含雪牛娘位移（胸口随之移动）再加自身微调。
        Vector3 recipientDelta = tuned.Recipient.RootWorldOffset - baseline.Recipient.RootWorldOffset - sourceDelta;
        Assert.Equal(0.2f, recipientDelta.X, 5);
        Assert.Equal(0.3f, recipientDelta.Z, 5);
    }

    [Fact]
    public void Metrics_FallbackAndBodyOnlyUseTopThirtyPercentAsHead()
    {
        PawnVisualMetrics fallback = MilkingPoseMath.FallbackMetrics();
        Assert.Equal(new Vector2(1.5f, 1.5f), fallback.BodySize);
        Assert.Equal(0.45f, fallback.HeadSize.X, 5);
        Assert.Equal(0.525f, fallback.HeadAnchor.Z, 5);
        Assert.False(fallback.HasExplicitHead);

        PawnVisualMetrics bodyOnly = MilkingPoseMath.BodyOnlyMetrics(new Vector2(2f, 1f));
        Assert.Equal(new Vector2(2f, 0.3f), bodyOnly.HeadSize);
        Assert.Equal(0.35f, bodyOnly.HeadAnchor.Z, 5);
    }
}
