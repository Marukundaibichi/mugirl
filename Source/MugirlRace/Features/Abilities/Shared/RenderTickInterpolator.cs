namespace Mugirl.Features.Abilities;

/// <summary>
/// 逐帧补间的显示 tick：只在主线程的绘制阶段推进，追随已模拟的游戏 tick，不预测未来状态。
/// 投掷物与扣篮道具共用；不存档，读档或切换状态后从模拟进度重新开始。默认值即“尚未采样、显示 tick 为 0”。
/// </summary>
internal struct RenderTickInterpolator
{
    private float ticks;
    private int frame;
    private bool sampled;

    /// <summary>当前用于绘制的 tick（可带小数）。</summary>
    internal readonly float Ticks => ticks;

    /// <summary>把显示 tick 设为 <paramref name="value"/>，下一帧重新开始采样。</summary>
    internal void Reset(float value = 0f)
    {
        ticks = value;
        frame = 0;
        sampled = false;
    }

    /// <summary>
    /// 开始一帧的采样；同一帧内重复调用返回 false。首次采样或跳过了至少一帧（离开视野后重新绘制）时，
    /// 先追到上一个已模拟的 tick，避免画在旧位置。
    /// </summary>
    internal bool BeginFrame(int currentFrame, int simulatedTicks)
    {
        if (sampled && frame == currentFrame)
        {
            return false;
        }

        if ((!sampled && simulatedTicks > 1) || (sampled && currentFrame > frame + 1))
        {
            ticks = UnityMathf.Max(ticks, simulatedTicks - 1f);
        }

        frame = currentFrame;
        sampled = true;
        return true;
    }

    /// <summary>
    /// 按本帧时长与倍速推进，结果限制在 [0, <paramref name="ceiling"/>]。负时长与负倍速按 0 处理；
    /// 卡顿帧的大时长也不会越过上限。
    /// </summary>
    internal void Advance(float frameSeconds, float tickRateMultiplier, float ceiling)
    {
        float frameTicks = UnityMathf.Max(0f, frameSeconds) * 60f * UnityMathf.Max(0f, tickRateMultiplier);
        ticks = UnityMathf.Clamp(ticks + frameTicks, 0f, ceiling);
    }
}
