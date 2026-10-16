namespace Mugirl.Features.Lances;

/// <summary>冲锋落点：沿途冲锋落在点击格；定点冲锋落在目标周围 8 格中离持枪者最近的可跳跃格。</summary>
internal static class LanceChargeDestinationUtility
{
    // StaticCacheLifecycle: 进程级只读偏移表，固定的 8 邻格顺序决定等距候选的取舍，不持有游戏对象。
    private static readonly IntVec3[] AdjacentOffsets =
    {
        new(-1, 0, -1), new(0, 0, -1), new(1, 0, -1),
        new(-1, 0, 0),                 new(1, 0, 0),
        new(-1, 0, 1),  new(0, 0, 1),  new(1, 0, 1),
    };

    internal static bool TryFindLandingCell(Pawn? pawn, IntVec3 targetCell, bool pointCharge, out IntVec3 destination)
    {
        destination = IntVec3.Invalid;
        Map? map = pawn?.Map;
        if (pawn is null || map is null || !targetCell.IsValid || !targetCell.InBounds(map))
        {
            return false;
        }

        if (!pointCharge)
        {
            if (!JumpUtility.ValidJumpTarget(pawn, map, targetCell))
            {
                return false;
            }

            destination = targetCell;
            return true;
        }

        int bestDistance = int.MaxValue;
        for (int index = 0; index < AdjacentOffsets.Length; index++)
        {
            IntVec3 candidate = targetCell + AdjacentOffsets[index];
            if (!JumpUtility.ValidJumpTarget(pawn, map, candidate))
            {
                continue;
            }

            int distance = candidate.DistanceToSquared(pawn.Position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                destination = candidate;
            }
        }

        return destination.IsValid;
    }
}
