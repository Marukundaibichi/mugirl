namespace Mugirl.Features.Abilities;

// 投掷物的飞行轨迹与举起时的绘制位置（纯函数）。位置为地图绘制坐标：x/z 为水平面，y 为绘制高度层。
internal static class ThrowFlightMath
{
    // 举起时物体在头顶上方的偏移与上下浮动。
    private const float HeldLift = 1.05f;
    private const float HeldBobAmplitude = 0.08f;
    private const float HeldBobRadiansPerTick = 0.12f;

    /// <summary>飞行总 tick：随距离增加，范围 22..75。</summary>
    internal static int FlightTicks(float distance)
    {
        return UnityMathf.RoundToInt(UnityMathf.Clamp(14f + distance * 2.2f, 22f, 75f));
    }

    /// <summary>飞行进度 [0, 1]；总时长无效时视为已到达。</summary>
    internal static float Progress(float elapsedTicks, int flightTicks)
    {
        return flightTicks <= 0 ? 1f : UnityMathf.Clamp01(elapsedTicks / flightTicks);
    }

    /// <summary>地面投影位置：用于屋顶穿透与扬尘，不含弧线高度。</summary>
    internal static Vector3 GroundPosition(Vector3 startGround, Vector3 destination, int ticksFlying, int flightTicks)
    {
        return UnityVector.Lerp(startGround, destination, Progress(ticksFlying, flightTicks));
    }

    /// <summary>
    /// 飞行中的绘制位置：从举起位置沿直线插值到落点，叠加抛物线高度（屏幕上向北偏移并抬高绘制层）。
    /// <paramref name="distance"/> 为起点格到落点格的距离，决定弧线高度。
    /// </summary>
    internal static Vector3 FlyingPosition(Vector3 startDraw, Vector3 destination, float renderTick, int flightTicks, float distance, float overheadAltitude)
    {
        float progress = Progress(renderTick, flightTicks);
        Vector3 ground = UnityVector.Lerp(startDraw, destination, progress);
        float arcHeight = UnityMathf.Clamp(distance * 0.32f, 2.2f, 6.5f);
        float height = 4f * progress * (1f - progress) * arcHeight;
        ground.Z += height * 0.55f;
        ground.Y = overheadAltitude + height;
        return ground;
    }

    /// <summary>举起时的绘制位置：持物者头顶上方随 tick 轻微浮动。</summary>
    internal static Vector3 HeldPosition(Vector3 carrierPos, int tick, float overheadAltitude)
    {
        Vector3 pos = carrierPos;
        pos.Z += HeldLift + TickAnimation.Sin(tick, HeldBobRadiansPerTick) * HeldBobAmplitude;
        pos.Y = overheadAltitude;
        return pos;
    }

    /// <summary>飞行中的自转角度 [0, 360)。</summary>
    internal static float SpinDegrees(float renderTick, float degreesPerTick)
    {
        return (renderTick * degreesPerTick) % 360f;
    }
}
