using Mugirl.Features.Milk;
using Mugirl.Features.Roping;

namespace Mugirl.Features.Incidents;

// 牧场布局：围栏圈地、牧草、木屋（床、桌椅、放雪牛奶的架子）与一排墙挂拴绳桩。
// 地图已经生成后才搭建：先清空区域（保留 Pawn 与草），迷雾中的格子不放建筑。
public partial class QuestPart_SpawnRunawayFarm
{
    private const int RanchHalfWidth = 13;
    private const int RanchHalfHeight = 10;
    private const int HouseHalfWidth = 4;
    private const int HouseHalfHeight = 3;
    private const int GateOffsetX = -4;
    private const float HaygrassChance = 0.45f;
    private const int ShelfMilkCount = 24;

    private static IntVec3 FindFarmCenter(Map map)
    {
        if (CellFinderLoose.TryFindRandomNotEdgeCellWith(
            22,
            c => c.Standable(map)
                && !c.Roofed(map)
                && CellRect.CenteredOn(c, RanchHalfWidth * 2 + 3, RanchHalfHeight * 2 + 3).InBounds(map),
            map,
            out IntVec3 result))
        {
            return result;
        }

        if (CellFinderLoose.TryFindRandomNotEdgeCellWith(
            18,
            c => c.Standable(map)
                && !c.Roofed(map),
            map,
            out result))
        {
            return result;
        }

        if (CellFinderLoose.TryFindRandomNotEdgeCellWith(
            10,
            c => c.Standable(map) && !c.Roofed(map),
            map,
            out result))
        {
            return result;
        }

        return map.Center;
    }

    private void BuildFarm(Map map, IntVec3 center)
    {
        Faction? buildingFaction = rancherFaction;

        ClearFarmArea(map, center);

        for (int x = -RanchHalfWidth; x <= RanchHalfWidth; x++)
        {
            for (int z = -RanchHalfHeight; z <= RanchHalfHeight; z++)
            {
                IntVec3 cell = center + new IntVec3(x, 0, z);
                if (!cell.InBounds(map))
                {
                    continue;
                }

                bool border = Math.Abs(x) == RanchHalfWidth || Math.Abs(z) == RanchHalfHeight;
                if (border)
                {
                    ThingDef borderDef = x == GateOffsetX && z == -RanchHalfHeight ? IncidentsDefOf.FenceGate : IncidentsDefOf.Fence;
                    TrackFarmThing(TrySpawnBuilding(borderDef, cell, map, buildingFaction, Rot4.North));
                }
                else if (cell.Standable(map) && !cell.Roofed(map) && Rand.Chance(HaygrassChance))
                {
                    TrackFarmThing(TrySpawnPlant(IncidentsDefOf.Plant_Haygrass, cell, map));
                }
            }
        }

        BuildRanchHouse(map, center + new IntVec3(-7, 0, 0), buildingFaction);
        BuildHitchWall(map, center + new IntVec3(5, 0, -2), buildingFaction, Rot4.East, RunawayCount);
    }

    private static void ClearFarmArea(Map map, IntVec3 center)
    {
        CellRect rect = CellRect.CenteredOn(center, RanchHalfWidth * 2 + 5, RanchHalfHeight * 2 + 5).ClipInsideMap(map);
        foreach (IntVec3 cell in rect)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing thing = things[i];
                if (thing == null || thing.Destroyed || ShouldKeepFarmAreaThing(thing))
                {
                    continue;
                }

                thing.Destroy(DestroyMode.Vanish);
            }

            map.roofGrid.SetRoof(cell, null);
        }
    }

    // 保留 Pawn 与天然草地（defName 含 Grass 的植物，序数比较）。
    private static bool ShouldKeepFarmAreaThing(Thing thing)
    {
        if (thing is Pawn)
        {
            return true;
        }

        return thing is Plant plant
            && plant.def?.defName?.Contains("Grass", StringComparison.Ordinal) == true;
    }

    private void BuildRanchHouse(Map map, IntVec3 center, Faction? buildingFaction)
    {
        for (int x = -HouseHalfWidth; x <= HouseHalfWidth; x++)
        {
            for (int z = -HouseHalfHeight; z <= HouseHalfHeight; z++)
            {
                IntVec3 cell = center + new IntVec3(x, 0, z);
                if (!cell.InBounds(map))
                {
                    continue;
                }

                bool border = Math.Abs(x) == HouseHalfWidth || Math.Abs(z) == HouseHalfHeight;
                if (!border)
                {
                    continue;
                }

                ThingDef def = x == HouseHalfWidth && z == 0 ? ThingDefOf.Door : ThingDefOf.Wall;
                TrackFarmThing(TrySpawnBuilding(def, cell, map, buildingFaction, Rot4.North));
            }
        }

        TrackFarmThing(TrySpawnBuilding(ThingDefOf.Bed, center + new IntVec3(-2, 0, -1), map, buildingFaction, Rot4.East));
        TrackFarmThing(TrySpawnBuilding(ThingDefOf.Table1x2c, center + new IntVec3(1, 0, 0), map, buildingFaction, Rot4.East));
        TrackFarmThing(TrySpawnBuilding(ThingDefOf.DiningChair, center + new IntVec3(1, 0, -2), map, buildingFaction, Rot4.North));
        TrackFarmThing(TrySpawnBuilding(ThingDefOf.DiningChair, center + new IntVec3(1, 0, 2), map, buildingFaction, Rot4.South));
        SpawnMilkShelf(map, center + new IntVec3(-3, 0, 2), buildingFaction, Rot4.South, ShelfMilkCount);
        SpawnMilkShelf(map, center + new IntVec3(3, 0, 2), buildingFaction, Rot4.South, ShelfMilkCount);
    }

    private void SpawnMilkShelf(Map map, IntVec3 cell, Faction? buildingFaction, Rot4 rot, int milkCount)
    {
        Thing? shelf = TrySpawnBuilding(ThingDefOf.ShelfSmall, cell, map, buildingFaction, rot);
        if (shelf == null)
        {
            return;
        }

        TrackFarmThing(shelf);
        Thing? milk = ThingMaker.MakeThing(MilkDefOf.Mugirl_Milk);
        if (milk == null)
        {
            return;
        }

        milk.stackCount = Math.Min(milkCount, milk.def.stackLimit);
        if (GenPlace.TryPlaceThing(milk, shelf.Position, map, ThingPlaceMode.Direct, out Thing placedMilk))
        {
            TrackFarmThing(placedMilk);
        }
    }

    // 墙在拴绳桩朝向的一侧；拴绳桩记入 hitches，完成判定只认这些桩。
    private void BuildHitchWall(Map map, IntVec3 start, Faction? buildingFaction, Rot4 hitchRot, int count)
    {
        IntVec3 wallStep = hitchRot == Rot4.East || hitchRot == Rot4.West ? IntVec3.North : IntVec3.East;
        IntVec3 wallOffset = hitchRot.FacingCell;
        for (int i = 0; i < count; i++)
        {
            IntVec3 hitchCell = start + wallStep * i;
            IntVec3 wallCell = hitchCell + wallOffset;
            TrackFarmThing(TrySpawnBuilding(ThingDefOf.Wall, wallCell, map, buildingFaction, Rot4.North));

            Thing? hitch = TrySpawnBuilding(RopingDefOf.Mugirl_WallRopeHitch, hitchCell, map, buildingFaction, hitchRot);
            if (hitch != null)
            {
                hitches.Add(hitch);
                TrackFarmThing(hitch);
            }
        }
    }

    private static Thing? TrySpawnBuilding(ThingDef def, IntVec3 cell, Map map, Faction? faction, Rot4 rot)
    {
        if (!cell.InBounds(map) || cell.Fogged(map))
        {
            return null;
        }

        if (!def.passability.Equals(Traversability.Impassable) && !cell.Standable(map))
        {
            return null;
        }

        GenSpawn.WipeExistingThings(cell, rot, def, map, DestroyMode.Vanish);
        ThingDef? stuff = def.MadeFromStuff ? ThingDefOf.WoodLog : null;
        Thing? thing = ThingMaker.MakeThing(def, stuff);
        if (thing == null)
        {
            return null;
        }

        if (faction != null && thing.def.CanHaveFaction)
        {
            thing.SetFaction(faction);
        }

        return GenSpawn.Spawn(thing, cell, map, rot);
    }

    private static Thing? TrySpawnPlant(ThingDef plantDef, IntVec3 cell, Map map)
    {
        if (!cell.InBounds(map) || cell.GetPlant(map) != null || !plantDef.CanEverPlantAt(cell, map))
        {
            return null;
        }

        Plant plant = (Plant)ThingMaker.MakeThing(plantDef);
        GenSpawn.Spawn(plant, cell, map);
        plant.Growth = Rand.Range(0.35f, 0.9f);
        return plant;
    }
}
