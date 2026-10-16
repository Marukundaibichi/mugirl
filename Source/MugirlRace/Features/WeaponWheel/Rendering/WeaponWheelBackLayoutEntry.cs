namespace Mugirl.Features.WeaponWheel;

// 普通背负武器的摆放参数，一个条目对应“武器序号 × 朝向”组合。
internal struct WeaponWheelBackLayoutEntry
{
    internal float OffsetX; // 相对 Pawn 绘制中心的横向偏移，东为正
    internal float OffsetZ; // 相对 Pawn 绘制中心的纵深偏移，北为正
    internal float Angle;   // 绕竖轴的旋转角（度）
    internal float Layer;   // PawnRenderNode 图层级，越大越靠前
    internal bool FlipHorizontal; // 旋转前贴图的左右镜像
    internal bool FlipVertical;   // 旋转前贴图的上下镜像
}
