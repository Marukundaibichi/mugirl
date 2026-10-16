namespace Mugirl.Features.Abilities;

// 投掷物与扣篮道具共用的载荷落地：先在首选格附近找落点，再退回原位置附近；生成异常时放回容器，载荷不会丢失。
internal static class PayloadPlacement
{
    // 落点搜索半径（格）。
    private const float LandingSearchRadius = 12f;

    /// <summary>
    /// 把 <paramref name="container"/> 中的第一个载荷放到地图上，返回生成后的实体；无落点或生成失败时返回 null，
    /// 载荷仍留在容器中。<paramref name="logKeyPrefix"/> 用于区分调用方的一次性警告。
    /// </summary>
    internal static Thing? TryPlace(ThingOwner<Thing>? container, IntVec3 preferred, IntVec3 fallback, Map? map, Rot4 rotation, string logKeyPrefix)
    {
        Thing? thing = container is { Count: > 0 } ? container[0] : null;
        if (container is null || thing is null || map is null)
        {
            return null;
        }

        if (!TryFindPlacementCell(thing, preferred, map, rotation, out IntVec3 cell)
            && !TryFindPlacementCell(thing, fallback, map, rotation, out cell))
        {
            return null;
        }

        container.Remove(thing);
        try
        {
            return GenSpawn.Spawn(thing, cell, map, rotation, WipeMode.VanishOrMoveAside);
        }
        catch (Exception ex)
        {
            container.TryAdd(thing);
            MugirlLog.WarningOnce(logKeyPrefix + thing.thingIDNumber, "Mugirl.Throw.PlaceFailed".Translate(thing.LabelCap, ex.Message).ToString());
            return null;
        }
    }

    // 按 RadialPattern 由近到远找第一个可直接生成载荷的格子。
    private static bool TryFindPlacementCell(Thing thing, IntVec3 preferred, Map map, Rot4 rotation, out IntVec3 result)
    {
        result = IntVec3.Invalid;
        int cells = GenRadial.NumCellsInRadius(LandingSearchRadius);
        for (int i = 0; i < cells; i++)
        {
            IntVec3 candidate = preferred + GenRadial.RadialPattern[i];
            if (CanPlaceAt(thing, candidate, map, rotation))
            {
                result = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool CanPlaceAt(Thing thing, IntVec3 cell, Map map, Rot4 rotation)
    {
        if (!cell.IsValid || !cell.InBounds(map))
        {
            return false;
        }

        if (thing.def.category == ThingCategory.Building)
        {
            // 蓝图判定可容许占格物；直接生成实体时会挪走或销毁它们。
            foreach (IntVec3 occupiedCell in GenAdj.OccupiedRect(cell, rotation, thing.def.Size).Cells)
            {
                if (!occupiedCell.InBounds(map))
                {
                    return false;
                }

                List<Thing> occupants = map.thingGrid.ThingsListAtFast(occupiedCell);
                for (int i = 0; i < occupants.Count; i++)
                {
                    Thing occupant = occupants[i];
                    if (occupant is Pawn || occupant.def.category == ThingCategory.Item
                        || occupant.def.category == ThingCategory.Building)
                    {
                        return false;
                    }
                }
            }

            AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(
                thing.def,
                cell,
                rotation,
                map,
                godMode: false,
                thingToIgnore: null,
                thing: thing,
                stuffDef: thing.Stuff,
                ignoreEdgeArea: true,
                ignoreInteractionSpots: true,
                ignoreClearableFreeBuildings: false);
            return report.Accepted;
        }

        if (thing is Pawn)
        {
            return cell.Standable(map) && cell.GetFirstPawn(map) is null;
        }

        if (thing.def.category == ThingCategory.Item)
        {
            Building? edifice = cell.GetEdifice(map);
            return cell.GetItemCount(map) < cell.GetMaxItemsAllowedInCell(map)
                && (edifice is null || edifice.def.surfaceType == SurfaceType.Item
                    || edifice.def.surfaceType == SurfaceType.Eat || edifice is Building_Door);
        }

        if (thing.def.category == ThingCategory.Plant)
        {
            return cell.Standable(map) && cell.GetEdifice(map) is null
                && cell.GetPlant(map) is null && thing.def.CanSpawnAt(cell, rotation, map);
        }

        return thing.def.CanSpawnAt(cell, rotation, map);
    }
}
