using System.Runtime.CompilerServices;

namespace Mugirl.Features.Roping;

/// <summary>待系状态按地图分组；旧地图的条目不会影响同一 Pawn 在另一张地图上的状态。</summary>
internal static class PendingHitchRegistry
{
    // StaticCacheLifecycle: 每局瞬态弱地图索引。读档/新局前清空，不阻止已移除地图被回收。
    private static readonly ConditionalWeakTable<Map, HashSet<Pawn>> Pending = new();
    // 仅作快速拒绝提示；地图回收可留下偏大的计数，查询仍由弱索引核实，语义不依赖计数精确。
    private static int totalPending;

    internal static bool AnyPending => totalPending > 0;

    internal static void Mark(Pawn pawn, Map map)
    {
        if (IsValid(pawn, map) && Pending.GetOrCreateValue(map).Add(pawn))
        {
            totalPending++;
        }
    }

    internal static void Clear(Pawn pawn, Map map)
    {
        if (Pending.TryGetValue(map, out HashSet<Pawn>? entries) && entries.Remove(pawn))
        {
            totalPending--;
        }
    }

    internal static bool Contains(Pawn pawn, Map map)
    {
        if (!Pending.TryGetValue(map, out HashSet<Pawn>? entries) || !entries.Contains(pawn))
        {
            return false;
        }

        if (IsValid(pawn, map) && pawn.roping?.IsRopedToSpot != true)
        {
            return true;
        }

        Clear(pawn, map);
        return false;
    }

    [GameLifecycleHook(GameLifecycleEvent.BeforeLoad, Order = 500)]
    [GameLifecycleHook(GameLifecycleEvent.BeforeNewGame, Order = 500)]
    internal static void Reset()
    {
        Pending.Clear();
        totalPending = 0;
    }

    private static bool IsValid(Pawn pawn, Map map) => pawn.Spawned && !pawn.Destroyed && pawn.Map == map;
}
