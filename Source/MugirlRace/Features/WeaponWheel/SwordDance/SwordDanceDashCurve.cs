namespace Mugirl.Features.WeaponWheel;

// 连携剑舞冲刺的纯曲线：停顿 → 后撤蓄力 → 加速冲过目标 → 冲过头减速 → 回到落点。
// 只依赖输入参数，供组件绘制与命中时机共用，并由单元测试覆盖。位置都在地图 XZ 平面（y 由调用方处理）。
internal static class SwordDanceDashCurve
{
    // 各阶段的结束进度（以 42 tick 的设计时长为分母）。
    internal const float PauseEnd = 5f / 42f;
    internal const float RecoilEnd = 15f / 42f;
    internal const float RushEnd = 24f / 42f;
    internal const float OvershootEnd = 31f / 42f;
    // 后撤与冲过头的距离（格）。
    internal const float BufferDistance = 0.5f;
    // 方向长度平方低于该值时视为原地，方向改为正北。
    internal const float MinDirectionSqrMagnitude = 0.0001f;

    // 冲刺进度 [0, 1]；时长至少按 1 tick 计。
    internal static float Progress(int elapsedTicks, int durationTicks)
    {
        int duration = Math.Max(1, durationTicks);
        return UnityMathf.Clamp01((float)elapsedTicks / duration);
    }

    // 水平冲刺方向：起点到终点（忽略 y）的单位向量；两点几乎重合时为正北（+Z）。
    internal static Vector3 Direction(Vector3 origin, Vector3 end)
    {
        Vector3 direction = end - origin;
        direction.Y = 0f;
        return UnityVector.SqrMagnitude(direction) > MinDirectionSqrMagnitude
            ? UnityVector.Normalized(direction)
            : UnityVector.Forward;
    }

    // 进度对应的绘制根位置。
    internal static Vector3 RootPosition(Vector3 origin, Vector3 destination, Vector3 direction, float progress)
    {
        if (progress < PauseEnd)
        {
            return origin;
        }

        Vector3 recoil = origin - direction * BufferDistance;
        if (progress < RecoilEnd)
        {
            float phase = UnityMathf.InverseLerp(PauseEnd, RecoilEnd, progress);
            return UnityVector.LerpUnclamped(origin, recoil, SmoothStep01(phase));
        }
        if (progress < RushEnd)
        {
            float phase = UnityMathf.InverseLerp(RecoilEnd, RushEnd, progress);
            return UnityVector.LerpUnclamped(recoil, destination, phase * phase);
        }

        Vector3 overshoot = destination + direction * BufferDistance;
        if (progress < OvershootEnd)
        {
            float phase = UnityMathf.InverseLerp(RushEnd, OvershootEnd, progress);
            float decelerating = 1f - (1f - phase) * (1f - phase);
            return UnityVector.LerpUnclamped(destination, overshoot, decelerating);
        }

        float returnPhase = UnityMathf.InverseLerp(OvershootEnd, 1f, progress);
        return UnityVector.LerpUnclamped(overshoot, destination, SmoothStep01(returnPhase));
    }

    // 冲刺越过目标中心的进度：目标在“后撤点 → 落点”线段上的投影比例，按冲刺段的二次加速换算。
    internal static float ImpactProgress(Vector3 origin, Vector3 destination, Vector3 direction, Vector3 targetPosition)
    {
        Vector3 recoil = origin - direction * BufferDistance;
        Vector3 rush = destination - recoil;
        float rushSqrMagnitude = UnityVector.SqrMagnitude(rush);
        float targetFraction = rushSqrMagnitude <= MinDirectionSqrMagnitude
            ? 0.5f
            : UnityMathf.Clamp01(UnityVector.Dot(targetPosition - recoil, rush) / rushSqrMagnitude);
        return UnityMathf.Lerp(RecoilEnd, RushEnd, UnityMathf.Sqrt(targetFraction));
    }

    // 冲刺中武器与朝向：冲过头之前朝前，回程朝反方向。
    internal static bool FacesForward(float progress) => progress < OvershootEnd;

    // 模组自己的平滑曲线 t²(3 - 2t)（1.6 原式，与 Unity Mathf.SmoothStep 的写法不同，保持不变）。
    internal static float SmoothStep01(float value)
    {
        value = UnityMathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }
}
