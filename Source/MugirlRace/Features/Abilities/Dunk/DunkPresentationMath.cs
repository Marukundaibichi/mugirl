namespace Mugirl.Features.Abilities;

// 扣篮演出各阶段的绘制位置与转角（纯函数）。位置为地图绘制坐标：x/z 为水平面，y 为绘制高度层。
// renderTick 为该阶段已经过的显示 tick（可带小数）。
internal static class DunkPresentationMath
{
    internal const int SpinTicks = 18;
    internal const int FlightTicksBeforeImpact = 31;
    internal const int SlamTicks = 10;
    internal const float OrbitDegreesPerTick = 36f;
    internal const float HeadSpinDegreesPerTick = 46f;

    // 运球时头每 tick 转 24°，15 tick 转满一圈。
    private const int DribbleTicksPerTurn = 15;
    private const float DribbleDegreesPerTick = 24f;

    /// <summary>运球时头的自转角 [0, 360)；后期 tick 很大时仍保持整数精度。</summary>
    internal static float DribbleSpinDegrees(int tick)
    {
        return TickAnimation.WrappedDegrees(tick, DribbleTicksPerTurn, DribbleDegreesPerTick);
    }

    /// <summary>运球位置：在施术者面前上下拍动并左右轻摆。</summary>
    internal static Vector3 DribblePosition(Vector3 carrierPos, Vector3 facing, int tick, float pawnAltitude)
    {
        float bounce = UnityMathf.Abs(TickAnimation.Sin(tick, 0.21f));
        Vector3 center = carrierPos + facing * 0.28f;
        center.X += TickAnimation.Sin(tick, 0.10f) * 0.10f;
        center.Z += -0.52f + bounce * 0.48f;
        center.Y = pawnAltitude + 0.08f + bounce * 0.01f;
        return center;
    }

    /// <summary>起跳前绕施术者盘旋上升的位置（不含施术者自身的位移）。<paramref name="liftedAltitude"/> 为升到顶时的绘制层。</summary>
    internal static Vector3 SpinPosition(Vector3 launchPos, float renderTick, float liftedAltitude)
    {
        float lift = UnityMathf.SmoothStep(0f, 1f, UnityMathf.Clamp01(renderTick / 5f));
        float radians = renderTick * OrbitDegreesPerTick * UnityMathf.Deg2Rad;
        Vector3 center = launchPos;
        center.X += UnityMathf.Cos(radians) * 0.55f * lift;
        center.Z += (1.05f + UnityMathf.Sin(radians) * 0.30f) * lift;
        center.Y = UnityMathf.Lerp(launchPos.Y, liftedAltitude, lift);
        return center;
    }

    /// <summary>随施术者飞行时，头在手边的小幅逐帧摆动。</summary>
    internal static Vector3 HeldWobble(Vector3 heldPos, float renderTick)
    {
        float radians = renderTick * HeadSpinDegreesPerTick * UnityMathf.Deg2Rad;
        heldPos.X += UnityMathf.Sin(radians) * 0.04f;
        heldPos.Z += (1f - UnityMathf.Cos(radians)) * 0.025f;
        return heldPos;
    }

    /// <summary>
    /// 头独立飞行的抛物线位置。<paramref name="distance"/> 为起点格到落点格的距离，决定弧顶高度。
    /// </summary>
    internal static Vector3 FlightPosition(Vector3 launchPos, Vector3 end, float distance, float renderTick)
    {
        float t = UnityMathf.Clamp01(renderTick / FlightTicksBeforeImpact);
        Vector3 pos = UnityVector.Lerp(launchPos, end, t);
        float apexHeight = UnityMathf.Clamp(5.6f + distance * 0.18f, 5.8f, 8.5f);
        float height = UnityMathf.Sin(t * UnityMathf.PI) * apexHeight;
        pos.Z += height * 0.62f;
        pos.Y = UnityMathf.Lerp(launchPos.Y, end.Y, t) + height * 0.075f;
        return pos;
    }

    /// <summary>下扣：从 <paramref name="movingStart"/> 加速砸向 <paramref name="end"/>。</summary>
    internal static Vector3 SlamPosition(Vector3 movingStart, Vector3 end, float renderTick)
    {
        float t = UnityMathf.Clamp01(renderTick / SlamTicks);
        float acceleratedDescent = 0.35f * t + 0.65f * t * t;
        return UnityVector.Lerp(movingStart, end, acceleratedDescent);
    }

    /// <summary>
    /// 下扣显示 tick 的上限：跟随飞行者下坠时可领先一 tick 以跨过状态切换，但头不能提前画到触地点；其余阶段不超过已模拟的 tick。
    /// </summary>
    internal static float PresentationCeiling(int stateTicks, bool slamTracksCarrier)
    {
        return slamTracksCarrier ? UnityMathf.Min(stateTicks + 1f, SlamTicks - 0.2f) : stateTicks;
    }
}
