namespace Mugirl.Features.Milk;

// 喂奶姿态的镜像微调值：X 随被喂者在左/右翻转，Z 保持屏幕方向，角度单位为度。
internal struct MilkFeedingLayoutEntry
{
    internal const float MaxOffset = 1f;
    internal const float MaxAngle = 45f;

    public float SourceX;
    public float SourceZ;
    public float RecipientX;
    public float RecipientZ;
    public float SourceAngle;
    public float RecipientAngle;

    // 位移限制在 ±1 格、角度限制在 ±45°，防止手写或过期的覆盖文件把 Pawn 摆出画面。
    internal static MilkFeedingLayoutEntry Clamp(MilkFeedingLayoutEntry value)
    {
        value.SourceX = UnityMathf.Clamp(value.SourceX, -MaxOffset, MaxOffset);
        value.SourceZ = UnityMathf.Clamp(value.SourceZ, -MaxOffset, MaxOffset);
        value.RecipientX = UnityMathf.Clamp(value.RecipientX, -MaxOffset, MaxOffset);
        value.RecipientZ = UnityMathf.Clamp(value.RecipientZ, -MaxOffset, MaxOffset);
        value.SourceAngle = UnityMathf.Clamp(value.SourceAngle, -MaxAngle, MaxAngle);
        value.RecipientAngle = UnityMathf.Clamp(value.RecipientAngle, -MaxAngle, MaxAngle);
        return value;
    }
}
