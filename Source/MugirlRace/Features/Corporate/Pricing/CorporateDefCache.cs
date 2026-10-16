using System.Threading;

namespace Mugirl.Features.Corporate;

/// <summary>
/// 只依赖 Def 的进程级惰性缓存。Def 在一次运行中不变，但切换语言会清空并重建全部 Def 对象（DefOf 随之重新绑定），
/// 所以以 <see cref="ThingDefOf.Silver"/> 的实例作为“Def 代数”标记，标记改变时重建。
/// 可在读档长事件的工作线程与主线程上使用：值与标记作为一个不可变快照整体发布。
/// </summary>
internal sealed class CorporateDefCache<T>(Func<T> factory) where T : class
{
    private readonly object sync = new object();
    private Snapshot? snapshot;

    internal T Value
    {
        get
        {
            ThingDef marker = ThingDefOf.Silver;
            Snapshot? current = Volatile.Read(ref snapshot);
            if (current is not null && ReferenceEquals(current.Marker, marker))
            {
                return current.Value;
            }

            lock (sync)
            {
                current = snapshot;
                if (current is null || !ReferenceEquals(current.Marker, marker))
                {
                    // 构建失败时不缓存，下次访问重试（与 1.6 每局重建的失败行为一致）。
                    current = new Snapshot(marker, factory());
                    Volatile.Write(ref snapshot, current);
                }

                return current.Value;
            }
        }
    }

    private sealed record Snapshot(ThingDef Marker, T Value);
}
