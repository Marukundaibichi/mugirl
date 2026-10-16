using System.Runtime.CompilerServices;

namespace Mugirl.Platform;

/// <summary>
/// 来自 RimWorld 1.7 引擎 Waylith 的时间，替代 <c>UnityEngine.Time</c>。
/// </summary>
/// <remarks>
/// 按效果需要跟随什么来选时钟：
/// <list type="bullet">
/// <item>游戏状态、暂停与速度：tick（<c>Find.TickManager.TicksGame</c>）或 <c>RealTime.UnpausedRealTime</c>。</item>
/// <item>暂停时也要继续的纯装饰动画、界面动画、缓存过期：<see cref="RealtimeSinceStartup"/> 或 <see cref="RealtimeSinceStartupAsDouble"/>。</item>
/// <item>每帧一次的工作：<see cref="FrameCount"/>。它在同一引擎帧的所有 GUI 事件中保持不变；UnityStubs 的 <c>Time.frameCount</c>
/// 会在同一帧的输入事件与 Repaint 之间加一，使“每帧一次”的逻辑在有输入的帧里执行两次。</item>
/// </list>
/// 不要与其他时钟的值相减或比较：UnityStubs 的 realtime 起点不同。镜像原版按 <c>Time.frameCount</c> 缓存的逻辑属于 ADR-006 的例外，
/// 见迁移指南“原版仍按兼容层帧号缓存”。
/// </remarks>
public static class EngineClock
{
    /// <summary>
    /// UnityStubs <c>Time.deltaTime</c> 使用的截断上限（<c>Time.maximumDeltaTime</c> 默认值 1/3 秒）。
    /// 需要复现 Unity <c>Time.deltaTime</c> 时传给 <see cref="ClampedDeltaTime"/>。
    /// </summary>
    public const float UnityMaximumDeltaTime = 1f / 3f;

    /// <summary>引擎启动以来的秒数（Stopwatch 实时读取，不受游戏暂停影响），单精度。替代 <c>Time.realtimeSinceStartup</c>。</summary>
    public static float RealtimeSinceStartup => Waylith.Time.RealtimeSinceStartup;

    /// <summary>
    /// 引擎启动以来的秒数，双精度。长时间运行后单精度只剩毫秒级分辨率，过期判断与动画相位优先用它。
    /// 替代 <c>Time.realtimeSinceStartupAsDouble</c>。
    /// </summary>
    public static double RealtimeSinceStartupAsDouble => Waylith.Time.RealtimeSinceStartupAsDouble;

    /// <summary>引擎帧号，每个引擎帧结束时加一；在一帧内的所有 GUI 事件中不变。替代 <c>Time.frameCount</c>。</summary>
    public static int FrameCount => Waylith.Time.FrameCount;

    /// <summary>
    /// 上一引擎帧的时长（秒，非负，不截断）。替代 <c>Time.unscaledDeltaTime</c>；Unity 的 <c>Time.deltaTime</c> 还会截断到 1/3 秒，
    /// 需要时用 <see cref="ClampedDeltaTime"/>。窗口失焦或卡顿后可能是很大的值，用于插值前应先截断。
    /// </summary>
    public static float DeltaTime => Waylith.Time.DeltaTime;

    /// <summary>截断到 [0, <paramref name="maxSeconds"/>] 的 <see cref="DeltaTime"/>。</summary>
    public static float ClampedDeltaTime(float maxSeconds) => ClampDelta(Waylith.Time.DeltaTime, maxSeconds);

    /// <summary>
    /// 把帧时长截断到 [0, <paramref name="maxSeconds"/>]；NaN 与负值得 0。按 Unity <c>Mathf.Clamp</c> 的顺序先比较下界，
    /// <paramref name="maxSeconds"/> 应为正数（为负时与 <c>Mathf.Clamp</c> 一样返回 <paramref name="maxSeconds"/>）。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ClampDelta(float deltaSeconds, float maxSeconds)
    {
        if (float.IsNaN(deltaSeconds) || deltaSeconds < 0f)
        {
            return 0f;
        }

        return deltaSeconds > maxSeconds ? maxSeconds : deltaSeconds;
    }
}
