using UnityEngine;
using Verse;

namespace MooGirl
{
    internal static class MooGirlMilkOutputUtility
    {
        internal static void SpawnStacksNear(ThingDef thingDef, int amount, IntVec3 position, Map map)
        {
            while (amount > 0)
            {
                int stack = Mathf.Clamp(amount, 1, thingDef.stackLimit);
                amount -= stack;

                Thing thing = ThingMaker.MakeThing(thingDef);
                thing.stackCount = stack;
                GenPlace.TryPlaceThing(thing, position, map, ThingPlaceMode.Near);
            }
        }
    }
}
