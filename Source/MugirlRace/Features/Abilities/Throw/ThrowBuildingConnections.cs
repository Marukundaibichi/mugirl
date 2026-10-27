namespace Mugirl.Features.Abilities;

// 只阻止拔起连成整体的结构；摆放相邻、普通设备接电都不构成结构连接。
internal static class ThrowBuildingConnections
{
    internal static bool IsConnected(Thing target)
    {
        Map map = target.Map;
        CellRect occupied = target.OccupiedRect();
        foreach (IntVec3 cell in occupied.Cells)
        {
            if (HasConnectionAt(target, cell, map))
            {
                return true;
            }
        }

        foreach (IntVec3 cell in occupied.AdjacentCellsCardinal)
        {
            if (HasConnectionAt(target, cell, map))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasConnectionAt(Thing target, IntVec3 cell, Map map)
    {
        if (!cell.InBounds(map))
        {
            return false;
        }

        bool sameFoundation = !ModsConfig.OdysseyActive
            || (map.terrainGrid.FoundationAt(cell)?.IsSubstructure ?? false)
            == (map.terrainGrid.FoundationAt(target.Position)?.IsSubstructure ?? false);
        List<Thing> neighbours = map.thingGrid.ThingsListAtFast(cell);
        for (int i = 0; i < neighbours.Count; i++)
        {
            Thing other = neighbours[i];
            if (other == target || !other.Spawned || other.def.category != ThingCategory.Building)
            {
                continue;
            }

            // 非对称连接也保护另一端（例如围栏连接门），但不把整格的标记混在一起。
            if (sameFoundation && (LinksTo(target.def, other.def) || LinksTo(other.def, target.def)))
            {
                return true;
            }

            // 只限制输电线本体。发电机、电池等可作为线缆的连接端，普通用电设备不因此被禁举。
            // 按输电能力而非当前供电状态判定，断电或关闭开关不会使线缆脱离结构。
            if (target.TryGetComp<CompPowerTransmitter>() is not null
                && other.TryGetComp<CompPower>()?.Props.transmitsPower == true)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool LinksTo(ThingDef source, ThingDef other)
    {
        GraphicData? graphic = source.graphicData;
        if (graphic is not { Linked: true })
        {
            return false;
        }

        // MapEdge 只是画到地图边缘，不是两个建筑之间的连接类别。
        LinkFlags otherFlags = (other.graphicData?.linkFlags ?? LinkFlags.None) & ~LinkFlags.MapEdge;
        if ((graphic.linkFlags & otherFlags) != LinkFlags.None)
        {
            return true;
        }

        return graphic.linkType == LinkDrawerType.Asymmetric
            && graphic.asymmetricLink is { } asymmetric
            && ((asymmetric.linkFlags & otherFlags) != LinkFlags.None
                || (asymmetric.linkToDoors && typeof(Building_Door).IsAssignableFrom(other.thingClass)));
    }
}
