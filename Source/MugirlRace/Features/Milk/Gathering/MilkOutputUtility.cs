namespace Mugirl.Features.Milk;

// 在指定格附近按堆叠上限分堆生成物品。
internal static class MilkOutputUtility
{
    internal static void SpawnStacksNear(ThingDef? thingDef, int amount, IntVec3 position, Map? map)
    {
        if (thingDef == null || map == null || !position.IsValid || amount <= 0)
        {
            return;
        }

        int stackLimit = Math.Max(1, thingDef.stackLimit);
        while (amount > 0)
        {
            int stack = NextStackSize(amount, stackLimit);
            amount -= stack;

            Thing thing = ThingMaker.MakeThing(thingDef);
            thing.stackCount = stack;
            GenPlace.TryPlaceThing(thing, position, map, ThingPlaceMode.Near);
        }
    }

    // 剩余数量 amount（>0）中下一堆的数量：不超过堆叠上限，至少 1。
    internal static int NextStackSize(int amount, int stackLimit) => UnityMathf.Clamp(amount, 1, Math.Max(1, stackLimit));
}
