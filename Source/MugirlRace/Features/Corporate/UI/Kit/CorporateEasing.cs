namespace Mugirl.Features.Corporate;

/// <summary>
/// 巨企终端所有过渡共用的缓动函数（窗口开关、页面切换、目录选择、结果淡入、提示与波纹）。
/// 纯计算，按 Unity 公式实现（<see cref="UnityMathf"/>），与 1.6 的结果逐位一致。
/// </summary>
internal static class CorporateEasing
{
    /// <summary>三次缓出 <c>1 - (1 - t)³</c>，<paramref name="t"/> 先钳制到 [0, 1]（1.6 <c>CorporateMotion.Ease</c>）。</summary>
    internal static float EaseOutCubic(float t)
    {
        t = UnityMathf.Clamp01(t);
        return 1f - UnityMathf.Pow(1f - t, 3f);
    }

    /// <summary>
    /// Hermite 平滑 <c>t²(3 - 2t)</c>，<paramref name="t"/> 先钳制到 [0, 1]（1.6 终端页面切换与对话框关闭曲线）。
    /// 不是 Unity 的 <c>Mathf.SmoothStep(from, to, t)</c>，两者求值顺序不同。
    /// </summary>
    internal static float SmoothStep01(float t)
    {
        t = UnityMathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// 指数逼近：每秒以速率 <paramref name="speed"/> 向 <paramref name="target"/> 靠近，距离小于 0.001 时直接到位，
    /// 与帧时长无关地收敛（1.6 <c>CorporateMotion.Approach</c>）。<paramref name="delta"/> 很大时一步到位。
    /// </summary>
    internal static float Approach(float value, float target, float speed, float delta)
    {
        float result = UnityMathf.Lerp(value, target, 1f - UnityMathf.Exp(-speed * delta));
        return UnityMathf.Abs(result - target) < 0.001f ? target : result;
    }
}
