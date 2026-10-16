namespace Mugirl.Features.Mounting;

// 下骑落点：优先指定格，其次载具周围 8 格，最后在半径 2~5 内随机找可站立、无人、可视的格子。
internal static class DismountCellFinder
{
    private const int MinRandomRadius = 2;
    private const int MaxRandomRadius = 5;

    internal static bool TryFindCell(Pawn carrier, Pawn? rider, IntVec3? preferredCell, out IntVec3 cell)
    {
        cell = IntVec3.Invalid;
        Map? map = carrier.Map;
        if (map is null)
        {
            return false;
        }

        if (preferredCell is { } preferred && IsValidCell(preferred, carrier, rider, map))
        {
            cell = preferred;
            return true;
        }

        foreach (IntVec3 adjacent in GenAdj.CellsAdjacent8Way(carrier))
        {
            if (IsValidCell(adjacent, carrier, rider, map))
            {
                cell = adjacent;
                return true;
            }
        }

        for (int radius = MinRandomRadius; radius <= MaxRandomRadius; radius++)
        {
            if (CellFinder.TryFindRandomCellNear(carrier.Position, map, radius, c => IsValidCell(c, carrier, rider, map), out cell))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsValidCell(IntVec3 c, Pawn? carrier, Pawn? rider, Map map)
    {
        if (!c.InBounds(map) || c.Fogged(map) || !c.Standable(map))
        {
            return false;
        }

        if (c.GetFirstPawn(map) is not null)
        {
            return false;
        }

        if (carrier is not null && !GenSight.LineOfSight(carrier.Position, c, map, skipFirstCell: true))
        {
            return false;
        }

        return rider is null || GenPlace.HaulPlaceBlockerIn(rider, c, map, checkBlueprintsAndFrames: false) is null;
    }
}
