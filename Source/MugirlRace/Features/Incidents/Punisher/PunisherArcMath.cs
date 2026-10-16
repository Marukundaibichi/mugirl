namespace Mugirl.Features.Incidents;

// 惩罚者死亡时战利品飞散的弧线（纯计算，按 Unity 1.6 公式，不经过 UnityStubs）。
internal static class PunisherArcMath
{
    /// <summary>动画持续的游戏 tick 数；最后一 tick 落地。</summary>
    internal const int DurationTicks = 28;

    private const float MinHorizontalPathSquared = 0.001f;
    private const float AltitudeOffset = 0.04f;
    private const float AltitudeLift = 0.10f;
    private const float SideNormalAngle = 90f;

    /// <summary>动画进度 [0, 1]：第 0 tick 为 0，第 DurationTicks - 1 tick 为 1。</summary>
    internal static float Progress(int ageTicks)
    {
        return UnityMathf.Clamp01(ageTicks / (float)(DurationTicks - 1));
    }

    /// <summary>
    /// t 时刻的绘制位置：起点到目标的直线，加上随正弦起落的侧向摆动与向屏幕上方（+Z）的抛起；
    /// Y 是绘制层高度，在 baseAltitude 上略微抬升。
    /// </summary>
    internal static Vector3 Evaluate(Vector3 origin, Vector3 target, float t, float sideSwing, float launchHeight, float baseAltitude)
    {
        Vector3 position = UnityVector.Lerp(origin, target, t);
        Vector3 path = target - origin;
        path.Y = 0f;
        // 侧向法线：水平路径绕 +Y 旋转 90°（与 Verse Vector3Utility.RotatedBy 一致）；起点与目标重合时取 +X。
        Vector3 normal = UnityVector.MagnitudeHorizontalSquared(path) > MinHorizontalPathSquared
            ? UnityVector.RotatedBy(UnityVector.Normalized(path), SideNormalAngle)
            : Vector3.UnitX;
        float lift = UnityMathf.Sin(t * UnityMathf.PI);
        position += normal * lift * sideSwing;
        position += UnityVector.Forward * lift * launchHeight;
        position.Y = baseAltitude + AltitudeOffset + lift * AltitudeLift;
        return position;
    }
}
