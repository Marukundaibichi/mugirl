using Mugirl.Platform;

namespace Mugirl.Features.Corporate;

/// <summary>
/// 巨企终端界面动画共用的实时时钟（不受游戏暂停影响）。
/// </summary>
/// <remarks>
/// 基于 Waylith 进程时钟的双精度秒数，按引擎帧取一次快照：同一帧的 Layout、输入与 Repaint 读到同一时间，
/// 多个控件的动画步调一致。时间戳一律存为 double，只把差值转成 float，长时间运行后 0.1–0.5 秒的过渡也不会出现
/// 1.6 单精度 <c>Time.realtimeSinceStartup</c> 的阶梯。
/// </remarks>
internal static class CorporateClock
{
    /// <summary>“从未开始”的时间戳：<see cref="Since"/> 得正无穷，相关动画视为早已结束。</summary>
    internal const double Never = double.NegativeInfinity;

    // StaticCacheLifecycle: 进程级单帧快照（帧号与秒数），不含游戏状态；每个引擎帧第一次读取时刷新，无需清理。
    private static int snapshotFrame = int.MinValue;
    private static double snapshot;

    /// <summary>本引擎帧的实时秒数（同一帧内不变）。</summary>
    internal static double Now
    {
        get
        {
            int frame = EngineClock.FrameCount;
            if (frame != snapshotFrame)
            {
                snapshotFrame = frame;
                snapshot = EngineClock.RealtimeSinceStartupAsDouble;
            }

            return snapshot;
        }
    }

    /// <summary>自 <paramref name="started"/> 起经过的秒数；<paramref name="started"/> 为 <see cref="Never"/> 时为正无穷。</summary>
    internal static float Since(double started) => (float)(Now - started);

    /// <summary>
    /// 周期相位 <c>(Now × rate) mod period</c>，在双精度中取模后再转 float，装饰动画在长时间运行后仍然平滑。
    /// <paramref name="rate"/> 为负时结果在 (-period, 0] 内。
    /// </summary>
    internal static float Phase(double rate, double period) => (float)(Now * rate % period);
}
