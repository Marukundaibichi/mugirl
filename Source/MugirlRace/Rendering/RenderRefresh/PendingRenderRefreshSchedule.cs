namespace Mugirl.Rendering;

/// <summary>
/// 延后渲染刷新的调度表：登记后第 <see cref="EarlyRefreshDelayTicks"/> tick 补刷一次，
/// 第 <see cref="RefreshWindowTicks"/> tick 最终兜底一次，然后移除。
/// 只记录 tick，不访问游戏状态；键是否仍有效以及如何刷新由调用方提供。
/// </summary>
internal sealed class PendingRenderRefreshSchedule<TKey>
    where TKey : class
{
    internal const int EarlyRefreshDelayTicks = 30;
    internal const int RefreshWindowTicks = 300;

    private readonly Dictionary<TKey, Entry> pending = new();
    private readonly List<TKey> dueKeys = new();
    private int nextDueTick = int.MaxValue;

    internal int Count => pending.Count;

    /// <summary>登记或重新登记一个键；已有的计划从当前 tick 重新开始。</summary>
    internal void Schedule(TKey key, int currentTick)
    {
        Entry entry = new(currentTick + EarlyRefreshDelayTicks, currentTick + RefreshWindowTicks);
        pending[key] = entry;
        if (entry.NextTick < nextDueTick)
        {
            nextDueTick = entry.NextTick;
        }
    }

    internal void Clear()
    {
        pending.Clear();
        dueKeys.Clear();
        nextDueTick = int.MaxValue;
    }

    /// <summary>
    /// 推进到 <paramref name="currentTick"/>：失效的键直接移除；到期的键先更新计划再调用 <paramref name="refresh"/>，
    /// 所以刷新回调里可以安全地重新登记任意键（包括正在处理的键）。
    /// </summary>
    internal void Tick(int currentTick, Func<TKey, bool> isValid, Action<TKey> refresh)
    {
        if (pending.Count == 0 || currentTick < nextDueTick)
        {
            return;
        }

        dueKeys.Clear();
        nextDueTick = int.MaxValue;
        foreach (KeyValuePair<TKey, Entry> pair in pending)
        {
            if (!isValid(pair.Key) || currentTick >= pair.Value.NextTick)
            {
                dueKeys.Add(pair.Key);
            }
            else if (pair.Value.NextTick < nextDueTick)
            {
                nextDueTick = pair.Value.NextTick;
            }
        }

        for (int i = 0; i < dueKeys.Count; i++)
        {
            TKey key = dueKeys[i];
            if (!pending.TryGetValue(key, out Entry entry))
            {
                continue;
            }

            if (!isValid(key))
            {
                pending.Remove(key);
                continue;
            }

            // 只保留登记后的首次延后刷新与最终兜底两次。
            bool refreshDue = currentTick >= entry.NextTick;
            if (refreshDue && currentTick >= entry.FinalTick)
            {
                pending.Remove(key);
            }
            else
            {
                if (refreshDue)
                {
                    entry = new Entry(entry.FinalTick, entry.FinalTick);
                    pending[key] = entry;
                }

                if (entry.NextTick < nextDueTick)
                {
                    nextDueTick = entry.NextTick;
                }
            }

            if (refreshDue)
            {
                refresh(key);
            }
        }

        dueKeys.Clear();
    }

    private readonly record struct Entry(int NextTick, int FinalTick);
}
