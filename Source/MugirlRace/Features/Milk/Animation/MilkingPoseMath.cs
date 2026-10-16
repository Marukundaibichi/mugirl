namespace Mugirl.Features.Milk;

// 挤奶与喂奶动画的纯几何计算，不访问 Pawn 或渲染器，便于离线测试。
// 向量为 System.Numerics（X 东、Y 高度、Z 北），角度单位为度；+ - * 与 Unity 逐位相同，归一化、旋转用 Unity 语义的 UnityVector。
internal static class MilkingPoseMath
{
    internal const int PulseDurationTicks = 18;

    // 方向向量长度平方低于此值时视为两点重合（1.6 的 sqrMagnitude 阈值）。
    internal const float DirectionEpsilonSquared = 0.0001f;

    internal const float FallbackBodySize = 1.5f;
    internal const float FallbackHeadRegionFraction = 0.3f;

    // 交互动画的接触点不是 Pawn 原点。喂奶时，被喂者的身体位于其地图格一侧，但头部需要跨过雪牛娘中心线落到另一侧的胸前。
    private const float HeldChestLateralScale = 0.16f;
    private const float HeldChestHeightScale = -0.1f;

    // 生物科技原版给小孩喂奶时，孩子的身体会明显横向倾倒；雪牛娘喂奶沿用同样的横向构图，按体型在约 62° 到 82° 之间变化。
    private const float HeldMinTilt = 62f;
    private const float HeldMaxTilt = 82f;

    // 由种子生成 [-1, 1] 的伪随机数（整数哈希），用于脉冲抖动；不消耗游戏 Rand。
    internal static float NoiseSigned(int seed)
    {
        unchecked
        {
            uint x = (uint)seed;
            x ^= x >> 16;
            x *= 0x7feb352dU;
            x ^= x >> 15;
            x *= 0x846ca68bU;
            x ^= x >> 16;
            return ((x & 65535U) / 32767.5f) - 1f;
        }
    }

    // 脉冲包络：持续 PulseDurationTicks 的半个正弦，范围外为 0。
    internal static float PulseEnvelope(int pulseAge)
    {
        if (pulseAge < 0 || pulseAge >= PulseDurationTicks)
        {
            return 0f;
        }

        return UnityMathf.Sin(pulseAge / (float)PulseDurationTicks * UnityMathf.PI);
    }

    // 一次脉冲的拉伸与抖动叠加到 offset/scale 上；strength 为角色强度（目标 1，辅助者 0.45）。
    internal static void AddPulseTransform(int pulseAge, int pulseSeed, float strength, ref Vector3 offset, ref Vector3 scale)
    {
        if (pulseAge < 0 || pulseAge >= PulseDurationTicks)
        {
            return;
        }

        float progress = pulseAge / (float)PulseDurationTicks;
        float envelope = PulseEnvelope(pulseAge);
        float stretch = UnityMathf.Sin(progress * UnityMathf.PI * 2f);
        scale = new Vector3(
            scale.X * (1f - stretch * 0.035f * strength),
            scale.Y,
            scale.Z * (1f + stretch * 0.075f * strength));

        int jitterStep = pulseAge / 2;
        Vector3 jitter = new(
            NoiseSigned(pulseSeed + jitterStep * 92821),
            0f,
            NoiseSigned(pulseSeed + jitterStep * 17137));
        if (UnityVector.SqrMagnitude(jitter) > DirectionEpsilonSquared)
        {
            offset += UnityVector.Normalized(jitter) * (envelope * 0.045f * strength);
        }
    }

    // 水平面上指向对方的单位向量；两点几乎重合时为零向量。
    internal static Vector3 HorizontalDirection(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        direction.Y = 0f;
        return UnityVector.SqrMagnitude(direction) < DirectionEpsilonSquared ? Vector3.Zero : UnityVector.Normalized(direction);
    }

    // 朝对方方向的侧倾角：对方在东侧时向西倾，限制在 ±maxAngle 内；没有方向时为 0。
    internal static float LeanAngleForDirection(Vector3 direction, float maxAngle)
    {
        if (UnityVector.IsApproximatelyZero(direction))
        {
            return 0f;
        }

        return UnityMathf.Clamp(0f - direction.X * maxAngle, 0f - maxAngle, maxAngle);
    }

    // 被喂者位于雪牛娘哪一侧：先看东西向，再看南北向，重合时按 thingIDNumber 奇偶固定一侧。
    internal static float InteractionSideSign(Vector3 sourcePos, Vector3 recipientPos, int recipientId)
    {
        float deltaX = recipientPos.X - sourcePos.X;
        if (UnityMathf.Abs(deltaX) > 0.05f)
        {
            return deltaX > 0f ? 1f : -1f;
        }

        float deltaZ = recipientPos.Z - sourcePos.Z;
        if (UnityMathf.Abs(deltaZ) > 0.05f)
        {
            return deltaZ > 0f ? 1f : -1f;
        }

        return recipientId % 2 == 0 ? 1f : -1f;
    }

    // 喂奶双人姿态：雪牛娘略靠向被喂者，被喂者按体型横向倾倒，并反解 Root 位移使旋转后的头部锚点落在雪牛娘胸前。
    internal static InteractionPairPose SolvePairPose(
        in PawnVisualMetrics source,
        in PawnVisualMetrics recipient,
        Vector3 sourceDrawPos,
        Vector3 recipientDrawPos,
        float sideSign,
        MilkFeedingLayoutEntry layout)
    {
        float sourceHeight = UnityMathf.Max(0.5f, source.BodySize.Y);
        float sourceWidth = UnityMathf.Max(0.5f, source.BodySize.X);
        float recipientHeight = UnityMathf.Max(0.35f, recipient.BodySize.Y);
        float recipientWidth = UnityMathf.Max(0.35f, recipient.BodySize.X);
        float recipientVisualHeight = UnityMathf.Max(recipientHeight, UnityMathf.Abs(recipient.HeadAnchor.Z) + recipient.HeadSize.Y * 0.5f);
        float recipientVisualWidth = UnityMathf.Max(recipientWidth, UnityMathf.Abs(recipient.HeadAnchor.X) * 2f + recipient.HeadSize.X);

        Vector3 sourceToRecipient = recipientDrawPos - sourceDrawPos;
        sourceToRecipient.Y = 0f;
        if (UnityVector.SqrMagnitude(sourceToRecipient) > DirectionEpsilonSquared)
        {
            sourceToRecipient = UnityVector.Normalized(sourceToRecipient);
        }

        float sourceAngle = 0f;
        Vector3 sourceOffset = sourceToRecipient * 0.11f;
        sourceOffset += new Vector3(sideSign * layout.SourceX, 0f, layout.SourceZ);
        sourceAngle += sideSign * layout.SourceAngle;
        float contactSideSign = 0f - sideSign;
        float contactHeight = sourceHeight * HeldChestHeightScale;
        Vector3 chestLocal = new(
            contactSideSign * UnityMathf.Clamp(sourceWidth * HeldChestLateralScale, 0.09f, 0.26f),
            0f,
            UnityMathf.Clamp(contactHeight, -0.18f, -0.04f));
        Vector3 chestWorld = sourceDrawPos + sourceOffset + UnityVector.RotatedBy(chestLocal, sourceAngle);

        float heightRatio = recipientVisualHeight / sourceHeight;
        float widthRatio = recipientVisualWidth / sourceWidth;
        float sizeFactor = UnityMathf.Clamp01((heightRatio - 0.45f) / 1.1f);
        float widthFactor = UnityMathf.Clamp01((widthRatio - 0.45f) / 1.1f);
        float tiltMagnitude = UnityMathf.Lerp(HeldMinTilt, HeldMaxTilt, sizeFactor * 0.78f + widthFactor * 0.22f);
        float recipientAngle = 0f - sideSign * (tiltMagnitude + layout.RecipientAngle);

        // 反解 Root 位移：旋转后的头部锚点必须落在雪牛娘的胸部锚点上。
        Vector3 rotatedHeadAnchor = UnityVector.RotatedBy(recipient.HeadAnchor, recipientAngle);
        Vector3 recipientOffset = chestWorld - recipientDrawPos - rotatedHeadAnchor;
        recipientOffset += new Vector3(sideSign * layout.RecipientX, 0f, layout.RecipientZ);
        recipientOffset.Y += 0.012f;

        return new InteractionPairPose(
            new InteractionPose(sourceOffset, sourceAngle, source.HasExplicitHead, sourceHeight),
            new InteractionPose(recipientOffset, recipientAngle, recipient.HasExplicitHead, recipientHeight));
    }

    // 取不到渲染树时的默认尺寸：1.5 格见方的人形身体，头部为顶部 30% 区域。
    internal static PawnVisualMetrics FallbackMetrics()
    {
        float headSize = FallbackBodySize * FallbackHeadRegionFraction;
        return new PawnVisualMetrics(
            new Vector2(FallbackBodySize, FallbackBodySize),
            new Vector2(headSize, headSize),
            new Vector3(0f, 0f, FallbackBodySize * (0.5f - FallbackHeadRegionFraction * 0.5f)),
            HasExplicitHead: false);
    }

    // 没有独立头节点时，把身体贴图顶部 30% 区域的中心视为头部。
    internal static PawnVisualMetrics BodyOnlyMetrics(Vector2 bodySize)
    {
        return new PawnVisualMetrics(
            bodySize,
            new Vector2(bodySize.X, bodySize.Y * FallbackHeadRegionFraction),
            new Vector3(0f, 0f, bodySize.Y * (0.5f - FallbackHeadRegionFraction * 0.5f)),
            HasExplicitHead: false);
    }
}
