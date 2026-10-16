namespace Mugirl.Features.Corporate;

/// <summary>
/// 目录选中项的切换动画：只淡出淡入详情面板，搜索框保持焦点、列表保持可用，玩家可以快速比较多个商品。
/// 新的选择在淡出结束（0.08 秒）时由 <see cref="Advance"/> 生效，随后 0.17 秒淡入。
/// </summary>
internal sealed class CorporateCatalogTransition
{
    private const float OutDuration = 0.08f;
    private const float InDuration = 0.17f;
    private const float ActiveDuration = 0.25f;

    private Action? pending;
    private double started = CorporateClock.Never;
    private float outgoing = 1f;
    private bool committed = true;

    /// <summary>过渡进行中（详情面板暂不响应输入）。</summary>
    internal bool Active => CorporateClock.Since(started) < ActiveDuration;

    /// <summary>详情面板当前的不透明度。</summary>
    internal float Opacity
    {
        get
        {
            float elapsed = CorporateClock.Since(started);
            return committed
                ? CorporateEasing.EaseOutCubic((elapsed - OutDuration) / InDuration)
                : outgoing * (1f - CorporateEasing.EaseOutCubic(elapsed / OutDuration));
        }
    }

    /// <summary>请求切换：<paramref name="apply"/> 在淡出结束时执行；连续请求时只保留最后一个。</summary>
    internal void Request(Action apply)
    {
        outgoing = Opacity;
        pending = apply;
        started = CorporateClock.Now;
        committed = false;
    }

    /// <summary>每次绘制详情前调用：淡出结束后执行待定的切换。</summary>
    internal void Advance()
    {
        if (committed || CorporateClock.Since(started) < OutDuration)
        {
            return;
        }

        Action? apply = pending;
        pending = null;
        committed = true;
        apply?.Invoke();
    }

    /// <summary>取消待定的切换并立即显示为完全不透明。</summary>
    internal void Clear()
    {
        pending = null;
        committed = true;
        started = CorporateClock.Never;
    }
}
