using System.Linq;

namespace Mugirl.Features.Corporate;

/// <summary>
/// 核聚变研究站的地图生成：固定蓝图、地表整备、带噪声边缘的清场与四向进场通道。
/// 只在站点地图生成（与调试动作）时运行，不保存任何状态。
/// </summary>
/// <remarks>
/// 清场边缘与通道的噪声使用 <see cref="UnityMathf.PerlinNoise"/>，与 1.7 原版 UnityStubs 的 Mathf.PerlinNoise 逐位相同；
/// 1.6 的原生 Unity Perlin 是另一种实现，所以同一地图的清场轮廓在 1.6 与 1.7 之间本就不同。
/// </remarks>
internal static class CorporateFusionSiteBuilder
{
    // 清场边缘：蓝图外 4 格必清，再按噪声向外最多延伸 7 格。
    private const float EdgeClearance = 4f;
    private const float EdgeNoiseReach = 7f;

    internal static void Spawn(Map map, IntVec3 center)
    {
        PrefabDef prefab = CorporateDefOf.Mugirl_CorporateFusionResearchSite
            ?? throw new InvalidOperationException("Corporate fusion research prefab is missing.");
        CellRect siteRect = GenAdj.OccupiedRect(center, Rot4.North, prefab.size);
        CellRect clearanceBounds = siteRect.ExpandedBy(12);
        clearanceBounds.ClipInsideMap(map);
        foreach (IntVec3 cell in siteRect.Cells)
        {
            if (!cell.InBounds(map))
            {
                throw new InvalidOperationException("Corporate fusion research prefab footprint exceeds the map bounds.");
            }

            ClearCell(map, cell, removePlants: true);
        }

        foreach (IntVec3 cell in clearanceBounds.Cells)
        {
            float distance = DistanceOutside(cell, siteRect);
            float edgeNoise = UnityMathf.PerlinNoise((cell.x * 0.115f) + 17.3f, (cell.z * 0.115f) + 41.9f);
            if (distance > 0f && distance <= EdgeClearance + (edgeNoise * EdgeNoiseReach))
            {
                ClearCell(map, cell, removePlants: false);
            }
        }

        ClearApproaches(map, siteRect);
        EnsureTerrainSupport(prefab, map, center);

        if (!PrefabUtility.CanSpawnPrefab(prefab, map, center, Rot4.North))
        {
            // 自定义地表或地图修饰器仍可能拒绝放置；整平本区域后继续生成，不能消耗任务并留下空图。
            MugirlLog.DiagnosticMessage("Corporate fusion research site requires forced terrain preparation: "
                + DescribeSpawnFailure(prefab, map, center));
            CellRect fallbackRect = siteRect.ExpandedBy(1);
            fallbackRect.ClipInsideMap(map);
            foreach (IntVec3 cell in fallbackRect)
            {
                ClearCell(map, cell, removePlants: true);
                if (map.terrainGrid.FoundationAt(cell) != null)
                {
                    map.terrainGrid.RemoveFoundation(cell, doLeavings: false);
                }

                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
            }

            EnsureTerrainSupport(prefab, map, center);
        }

        PrefabUtility.SpawnPrefab(prefab, map, center, Rot4.North, onSpawned: InitializeThing);
        EnsureThings(prefab, map, center);
    }

    private static void EnsureThings(PrefabDef prefab, Map map, IntVec3 center)
    {
        // 原版 SpawnPrefab 会静默跳过不能正常放置的单件物品。此固定蓝图必须完整生成；
        // 补建只针对本次生成中缺失的条目，保留已经生成的建筑，不对旧地图执行。
        foreach (var entry in PrefabUtility.GetThings(prefab, center, Rot4.North))
        {
            PrefabThingData data = entry.Item1;
            IntVec3 cell = entry.Item2;
            if (cell.GetThingList(map).Any(existing => existing.def == data.def && existing.Position == cell))
            {
                continue;
            }

            Thing thing = ThingMaker.MakeThing(data.def, data.stuff);
            thing.stackCount = data.stackCountRange.RandomInRange;
            if (data.hp > 0)
            {
                thing.HitPoints = data.hp;
            }

            if (data.quality.HasValue)
            {
                thing.TryGetComp<CompQuality>()?.SetQuality(data.quality.Value, ArtGenerationContext.Outsider);
            }

            GenSpawn.Spawn(thing, cell, map, entry.Item3);
            InitializeThing(thing);
        }
    }

    private static void EnsureTerrainSupport(PrefabDef prefab, Map map, IntVec3 center)
    {
        // CanSpawnPrefab 先检查当前地面，SpawnPrefab 才铺设 terrain。
        // 先铺研究站的固定地板，避免薄冰等原地表在地板落地前阻止整座站点生成。
        IntVec3 root = PrefabUtility.GetRoot(prefab, center, Rot4.North);
        foreach (var entry in prefab.GetTerrain())
        {
            IntVec3 cell = root + entry.cell;
            if (!cell.InBounds(map))
            {
                throw new InvalidOperationException("Corporate fusion research floor plan exceeds the map bounds.");
            }

            // 薄冰属于独立的临时地形层，仅 SetTerrain 不会移除它，也不会露出新地板。
            map.terrainGrid.RemoveTempTerrain(cell, doLeavings: false, preventDestroyEffects: true);
            map.terrainGrid.SetTerrain(cell, entry.data.def);
        }

        foreach (var entry in PrefabUtility.GetThings(prefab, center, Rot4.North))
        {
            PrefabThingData data = entry.Item1;
            if (data.def.category != ThingCategory.Building || data.def.building?.isAttachment == true)
            {
                continue;
            }

            TerrainAffordanceDef? affordance = data.def.GetTerrainAffordanceNeed(data.stuff);
            if (affordance == null)
            {
                continue;
            }

            foreach (IntVec3 cell in GenAdj.OccupiedRect(entry.Item2, entry.Item3, data.def.Size))
            {
                if (!cell.InBounds(map))
                {
                    throw new InvalidOperationException("Corporate fusion research building footprint exceeds the map bounds.");
                }

                map.terrainGrid.RemoveTempTerrain(cell, doLeavings: false, preventDestroyEffects: true);
                // FoundationAt 优先于表层 TerrainAt 提供承载力。
                // 仅铺 Concrete 会留下轻型桥梁等 foundation，原版仍会判定建筑不可放置。
                TerrainDef? foundation = map.terrainGrid.FoundationAt(cell);
                if (foundation != null && !foundation.affordances.Contains(affordance))
                {
                    map.terrainGrid.RemoveFoundation(cell, doLeavings: false);
                }

                if (!cell.GetAffordances(map).Contains(affordance))
                {
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
                }
            }
        }
    }

    private static void InitializeThing(Thing thing)
    {
        CompRefuelable? refuelable = thing.TryGetComp<CompRefuelable>();
        if (refuelable != null && !refuelable.IsFull)
        {
            refuelable.Refuel(refuelable.Props.fuelCapacity);
        }
    }

    private static string DescribeSpawnFailure(PrefabDef prefab, Map map, IntVec3 center)
    {
        foreach (var entry in PrefabUtility.GetThings(prefab, center, Rot4.North))
        {
            PrefabThingData data = entry.Item1;
            IntVec3 cell = entry.Item2;
            Rot4 rot = entry.Item3;
            if ((data.def.building == null || !data.def.building.isAttachment)
                && !GenSpawn.CanSpawnAt(data.def, cell, map, rot))
            {
                if (data.def.category == ThingCategory.Building && !GenConstruct.CanBuildOnTerrain(data.def, cell, map, rot))
                {
                    return "Corporate fusion research prefab terrain validation failed for " + data.def.defName + " at " + cell
                        + " on terrain " + cell.GetTerrain(map).defName + ".";
                }

                if (data.def.HasSingleOrMultipleInteractionCells && !GenConstruct.InteractionCellStandable(data.def, cell, rot, map))
                {
                    return "Corporate fusion research prefab interaction cells are blocked for " + data.def.defName + " at " + cell + ".";
                }

                if (!GenConstruct.NotBlockingAnyInteractionCells(data.def, cell, rot, map))
                {
                    return "Corporate fusion research prefab would block an existing interaction cell near " + data.def.defName + " at " + cell + ".";
                }

                string blockers = string.Join(", ", cell.GetThingList(map).Select(t => t.def.defName).Distinct());
                return "Corporate fusion research prefab cannot spawn " + data.def.defName + " at " + cell
                    + " on terrain " + cell.GetTerrain(map).defName
                    + (blockers.NullOrEmpty() ? "." : "; cell contains: " + blockers + ".");
            }
        }

        return "Corporate fusion research prefab failed validation for an unknown nested-prefab condition.";
    }

    private static void ClearApproaches(Map map, CellRect siteRect)
    {
        int centerX = (siteRect.minX + siteRect.maxX) / 2;
        int centerZ = (siteRect.minZ + siteRect.maxZ) / 2;
        for (int z = 0; z <= siteRect.minZ; z++)
        {
            ClearVerticalApproach(map, centerX, z, 3.7f);
        }

        for (int z = siteRect.maxZ; z < map.Size.z; z++)
        {
            ClearVerticalApproach(map, centerX, z, 29.1f);
        }

        for (int x = 0; x <= siteRect.minX; x++)
        {
            ClearHorizontalApproach(map, centerZ, x, 53.4f);
        }

        for (int x = siteRect.maxX; x < map.Size.x; x++)
        {
            ClearHorizontalApproach(map, centerZ, x, 77.8f);
        }
    }

    private static void ClearVerticalApproach(Map map, int centerX, int z, float seed)
    {
        int offset = ApproachOffset(z, seed);
        int halfWidth = ApproachHalfWidth(z, seed);
        for (int x = centerX + offset - halfWidth; x <= centerX + offset + halfWidth; x++)
        {
            ClearCell(map, new IntVec3(x, 0, z), removePlants: false);
        }
    }

    private static void ClearHorizontalApproach(Map map, int centerZ, int x, float seed)
    {
        int offset = ApproachOffset(x, seed);
        int halfWidth = ApproachHalfWidth(x, seed);
        for (int z = centerZ + offset - halfWidth; z <= centerZ + offset + halfWidth; z++)
        {
            ClearCell(map, new IntVec3(x, 0, z), removePlants: false);
        }
    }

    // 通道中心线沿行进方向在 ±4 格内蜿蜒；噪声值在 [0,1]，取整结果有界。
    private static int ApproachOffset(int position, float seed)
        => UnityMathf.RoundToInt((UnityMathf.PerlinNoise((position * 0.055f) + seed, seed) - 0.5f) * 8f);

    private static int ApproachHalfWidth(int position, float seed)
        => UnityMathf.PerlinNoise((position * 0.12f) + seed, seed + 11f) > 0.68f ? 2 : 1;

    private static float DistanceOutside(IntVec3 cell, CellRect rect)
    {
        int dx = cell.x < rect.minX ? rect.minX - cell.x : cell.x > rect.maxX ? cell.x - rect.maxX : 0;
        int dz = cell.z < rect.minZ ? rect.minZ - cell.z : cell.z > rect.maxZ ? cell.z - rect.maxZ : 0;
        return UnityMathf.Sqrt((dx * dx) + (dz * dz));
    }

    internal static void ClearCell(Map map, IntVec3 cell, bool removePlants)
    {
        if (!cell.InBounds(map))
        {
            return;
        }

        // 摧毁矿物与建筑可能在拍下物品列表后又留下新碎石，所以重复有限的几轮。
        // 石块等 PassThroughOnly 物品也会让 GenSpawn.CanSpawnAt 拒绝本可清除的蓝图建筑。
        var blockers = new List<Thing>();
        for (int pass = 0; pass < 4; pass++)
        {
            blockers.Clear();
            foreach (Thing thing in cell.GetThingList(map))
            {
                if (ShouldClear(thing, removePlants))
                {
                    blockers.Add(thing);
                }
            }

            if (blockers.Count == 0)
            {
                break;
            }

            foreach (Thing thing in blockers)
            {
                if (!thing.Destroyed)
                {
                    DestroyBlocker(thing);
                }
            }
        }

        map.roofGrid.SetRoof(cell, null);
        // 临时冰层或其他临时地表会遮住新地板，也可能在人员进场后融化成水。
        map.terrainGrid.RemoveTempTerrain(cell, doLeavings: false, preventDestroyEffects: true);
        if (cell.GetTerrain(map).passability == Traversability.Impassable)
        {
            if (map.terrainGrid.FoundationAt(cell) != null)
            {
                map.terrainGrid.RemoveFoundation(cell, doLeavings: false);
            }

            map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
        }
    }

    private static bool ShouldClear(Thing thing, bool removePlants)
    {
        if (thing == null || thing.Destroyed || thing is Pawn)
        {
            return false;
        }

        return thing is Building
            || thing.def.category == ThingCategory.Filth
            || thing.def.entityDefToBuild is TerrainDef
            || (removePlants && thing.def.category == ThingCategory.Plant)
            || thing.def.passability != Traversability.Standable;
    }

    private static void DestroyBlocker(Thing thing)
    {
        if (thing.def.destroyable)
        {
            thing.Destroy(DestroyMode.Vanish);
            return;
        }

        // 蒸汽喷口等天然障碍在游戏中不可摧毁；原版 LayoutWorker 清理结构场地时也临时打开同一开关。
        bool previous = Thing.allowDestroyNonDestroyable;
        try
        {
            Thing.allowDestroyNonDestroyable = true;
            thing.Destroy(DestroyMode.Vanish);
        }
        finally
        {
            Thing.allowDestroyNonDestroyable = previous;
        }
    }
}
