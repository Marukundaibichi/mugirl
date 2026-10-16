using System.Diagnostics.CodeAnalysis;
using Verse.AI;

namespace Mugirl.Features.Roping;

// 牵引规则与查询入口。牵引关系的唯一真值是原版 Pawn_RopeTracker；模组只额外维护“待系到墙绳桩”状态（按地图分组的瞬态索引）。
// 1.6 的查询返回“tracker 或地图镜像”，镜像只会滞后于 tracker，因此只可能多报已经断开的牵引；1.7 直接读 tracker。
public static class RopingService
{
    // 牵引规则保留历史边界：按 Mugirl body 识别 ropee，允许 HAR 变体共用该身体（上骑规则按 IsMugirlPawn）。
    public static bool IsMugirlRopee([NotNullWhen(true)] Pawn? pawn)
    {
        return MugirlIdentity.HasMugirlBody(pawn);
    }

    public static bool CanStartPawnRope([NotNullWhen(true)] Pawn? roper, [NotNullWhen(true)] Pawn? ropee)
    {
        if (roper is null || ropee is null || roper == ropee || !IsMugirlRopee(ropee))
        {
            return false;
        }

        if (!roper.Spawned || !ropee.Spawned || roper.Dead || ropee.Dead || roper.Destroyed || ropee.Destroyed || roper.Map != ropee.Map)
        {
            return false;
        }

        if (roper.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) != true
            || ropee.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) != true)
        {
            return false;
        }

        if (IsFollowingRoper(roper))
        {
            return false;
        }

        if (IsFollowingRoper(ropee) && !IsRopedToSpot(ropee))
        {
            return false;
        }

        if (ropee.stances?.stunner?.Stunned == true)
        {
            return false;
        }

        if (IsMugirlRopee(roper))
        {
            return false;
        }

        return !(ropee.InMentalState && ropee.MentalState is MentalState_Berserk);
    }

    public static bool IsFollowingRoper([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn?.CurJob?.def == RopingDefOf.Mugirl_FollowRoper;
    }

    public static bool IsRopedByPawn([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn?.roping?.IsRopedByPawn == true;
    }

    public static Pawn? RoperFor(Pawn? ropee)
    {
        return ropee?.roping?.RopedByPawn;
    }

    public static bool IsRopedToSpot([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn?.roping?.IsRopedToSpot == true;
    }

    public static bool HasAnyRope([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn?.roping?.HasAnyRope == true || IsPendingSpotRope(pawn);
    }

    public static bool IsBlockedFromEscape([NotNullWhen(true)] Pawn? pawn)
    {
        return IsRopedToSpot(pawn) || IsPendingSpotRope(pawn);
    }

    public static void BreakAllRopesAndNotify(Pawn? pawn)
    {
        if (pawn is null)
        {
            return;
        }

        pawn.roping?.BreakAllRopes();
        ClearPendingSpotRope(pawn);
    }

    public static void DropPawnRopeAndNotify(Pawn? roper, Pawn? ropee)
    {
        if (ropee is null)
        {
            return;
        }

        // 优先使用 tracker 中的实际牵引者；传入的 roper 只在 tracker 已无牵引者时兜底。
        Pawn? ropeOwner = ropee.roping?.RopedByPawn ?? roper;
        ropeOwner?.roping?.DropRope(ropee);
        ClearPendingSpotRope(ropee);
    }

    // 牵引因断绳、无法到达或拴点消失而结束时调用：清掉可能残留的待系状态。
    public static void NotifyPawnNoLongerRopedToTarget(Pawn? pawn)
    {
        ClearPendingSpotRope(pawn);
    }

    public static void MarkPendingSpotRope(Pawn? pawn)
    {
        if (pawn?.Map is { } map)
        {
            PendingHitchRegistry.Mark(pawn, map);
        }
    }

    public static void ClearPendingSpotRope(Pawn? pawn)
    {
        if (PendingHitchRegistry.AnyPending && pawn?.Map is { } map)
        {
            PendingHitchRegistry.Clear(pawn, map);
        }
    }

    // 系绳 job 结束时 ropee 可能已经离开登记时的地图：两张地图都清理。
    public static void ClearPendingSpotRope(Pawn? pawn, Map? map)
    {
        if (pawn is null || !PendingHitchRegistry.AnyPending)
        {
            return;
        }

        if (map is not null)
        {
            PendingHitchRegistry.Clear(pawn, map);
        }
        if (pawn.Map is not null && pawn.Map != map)
        {
            ClearPendingSpotRope(pawn);
        }
    }

    public static bool IsPendingSpotRope([NotNullWhen(true)] Pawn? pawn)
    {
        // 待系状态只在系绳 job 的同一 tick 内存在；绝大多数查询在这里直接返回，避免进入地图索引查找。
        return PendingHitchRegistry.AnyPending
            && pawn?.Map is { } map
            && PendingHitchRegistry.Contains(pawn, map);
    }

    // 墙绳桩被拆除或销毁时，解开系在该格上的雪牛娘（事件很少，允许遍历全图 Pawn）。
    public static void NotifyWallRopeHitchRemoved(Map? map, IntVec3 cell)
    {
        IReadOnlyList<Pawn>? pawns = map?.mapPawns?.AllPawnsSpawned;
        if (pawns is null || !cell.IsValid)
        {
            return;
        }

        for (int i = 0; i < pawns.Count; i++)
        {
            Pawn pawn = pawns[i];
            if (!IsMugirlRopee(pawn) || pawn.roping is not { IsRopedToSpot: true } tracker || tracker.RopedTo.Cell != cell)
            {
                continue;
            }

            tracker.UnropeFromSpot();
            NotifyPawnNoLongerRopedToTarget(pawn);
        }
    }

    // 系到建筑时默认选中原版牵引列表中第一位正在跟随该牵引者的雪牛娘。
    public static Pawn? FirstMugirlFollowing(Pawn? roper)
    {
        if (roper?.roping?.Ropees is not { } ropees)
        {
            return null;
        }

        for (int i = 0; i < ropees.Count; i++)
        {
            if (IsMugirlFollowerOf(ropees[i], roper))
            {
                return ropees[i];
            }
        }

        return null;
    }

    // 统计该牵引者名下正在跟随的雪牛娘，数到 stopAt 即停止。
    public static int CountMugirlFollowers(Pawn? roper, int stopAt)
    {
        if (roper?.roping?.Ropees is not { } ropees)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < ropees.Count; i++)
        {
            if (IsMugirlFollowerOf(ropees[i], roper) && ++count >= stopAt)
            {
                break;
            }
        }

        return count;
    }

    public static bool HasOnlyMugirlRopees(Pawn_RopeTracker? tracker)
    {
        List<Pawn>? ropees = tracker?.Ropees;
        if (ropees is null || ropees.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < ropees.Count; i++)
        {
            if (!IsMugirlRopee(ropees[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsMugirlFollowerOf(Pawn? ropee, Pawn roper)
    {
        return ropee?.roping?.RopedByPawn == roper
            && IsMugirlRopee(ropee)
            && IsFollowingRoper(ropee)
            && ropee.CurJob.targetA.Thing == roper;
    }
}
