using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace MooGirl
{
    public class AdultContentExtension : DefModExtension
    {
        public bool adultOnly = true;
    }

    public class CompProperties_AdultContentControl : CompProperties
    {
        public CompProperties_AdultContentControl()
        {
            compClass = typeof(CompAdultContentControl);
        }
    }

    public class CompAdultContentControl : ThingComp
    {
        public override void PostPostMake()
        {
            base.PostPostMake();
            DestroyIfDisabled();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            DestroyIfDisabled();
        }

        public override void PostPostGeneratedForTrader(TraderKindDef trader, PlanetTile forTile, Faction forFaction)
        {
            base.PostPostGeneratedForTrader(trader, forTile, forFaction);
            DestroyIfDisabled();
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            DestroyIfDisabled();
        }

        private void DestroyIfDisabled()
        {
            if (!AdultContentUtility.FilterThing(parent))
            {
                return;
            }

            if (parent is Apparel apparel && apparel.Wearer != null)
            {
                apparel.Wearer.apparel?.Remove(apparel);
            }
            else if (parent.holdingOwner != null)
            {
                parent.holdingOwner.Remove(parent);
            }

            if (!parent.Destroyed)
            {
                parent.Destroy(DestroyMode.Vanish);
            }
        }
    }

    public static class AdultContentUtility
    {
        public static bool AdultContentEnabled => MooGirlMod.settings?.enableAdultContent ?? false;

        public static bool IsAdultContent(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            AdultContentExtension extension = def.GetModExtension<AdultContentExtension>();
            return extension != null && extension.adultOnly;
        }

        public static bool IsAllowed(ThingDef def)
        {
            return !IsAdultContent(def) || AdultContentEnabled;
        }

        public static bool FilterThing(Thing thing)
        {
            return thing != null && IsAdultContent(thing.def) && !AdultContentEnabled;
        }

        public static List<ThingDef> FilterThingDefs(IEnumerable<ThingDef> source)
        {
            return source.Where(IsAllowed).ToList();
        }

        public static void CleanupAllAdultContent(bool removeWornApparel)
        {
            if (Current.Game == null || AdultContentEnabled)
            {
                return;
            }

            CleanupMaps(removeWornApparel);
            CleanupCaravans(removeWornApparel);
            CleanupTradeShips();
            CleanupSettlements();
        }

        private static void CleanupMaps(bool removeWornApparel)
        {
            List<Map> maps = Current.Game.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                List<Thing> things = map.listerThings.AllThings.ToList();
                for (int j = things.Count - 1; j >= 0; j--)
                {
                    Thing thing = things[j];
                    if (thing.Destroyed || thing.holdingOwner != null)
                    {
                        continue;
                    }

                    if (FilterThing(thing))
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }

                List<Pawn> pawns = map.mapPawns.AllPawnsSpawned.ToList();
                for (int k = 0; k < pawns.Count; k++)
                {
                    CleanupPawn(pawns[k], removeWornApparel);
                }

                for (int s = map.passingShipManager.passingShips.Count - 1; s >= 0; s--)
                {
                    if (map.passingShipManager.passingShips[s] is TradeShip tradeShip)
                    {
                        CleanupThingHolder(tradeShip.GetDirectlyHeldThings(), removeWornApparel: false);
                    }
                }
            }
        }

        private static void CleanupCaravans(bool removeWornApparel)
        {
            List<Caravan> caravans = Find.WorldObjects.Caravans;
            for (int i = 0; i < caravans.Count; i++)
            {
                Caravan caravan = caravans[i];
                List<Pawn> pawns = caravan.PawnsListForReading.ToList();
                for (int j = 0; j < pawns.Count; j++)
                {
                    CleanupPawn(pawns[j], removeWornApparel);
                }
            }
        }

        private static void CleanupTradeShips()
        {
            List<Map> maps = Current.Game.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                PassingShipManager manager = maps[i].passingShipManager;
                for (int j = manager.passingShips.Count - 1; j >= 0; j--)
                {
                    if (manager.passingShips[j] is TradeShip tradeShip)
                    {
                        CleanupThingHolder(tradeShip.GetDirectlyHeldThings(), removeWornApparel: false);
                    }
                }
            }
        }

        private static void CleanupSettlements()
        {
            List<WorldObject> worldObjects = Find.WorldObjects.AllWorldObjects;
            for (int i = 0; i < worldObjects.Count; i++)
            {
                if (worldObjects[i] is Settlement settlement)
                {
                    settlement.trader?.TryDestroyStock();
                }
            }
        }

        private static void CleanupPawn(Pawn pawn, bool removeWornApparel)
        {
            if (pawn == null)
            {
                return;
            }

            if (removeWornApparel && pawn.apparel != null)
            {
                List<Apparel> worn = pawn.apparel.WornApparel.Where(app => FilterThing(app)).ToList();
                for (int i = 0; i < worn.Count; i++)
                {
                    Apparel apparel = worn[i];
                    pawn.apparel.Remove(apparel);
                    if (!apparel.Destroyed)
                    {
                        apparel.Destroy(DestroyMode.Vanish);
                    }
                }
            }

            if (pawn.inventory != null)
            {
                CleanupThingHolder(pawn.inventory.innerContainer, removeWornApparel: false);
            }

            if (pawn.carryTracker?.innerContainer != null)
            {
                CleanupThingHolder(pawn.carryTracker.innerContainer, removeWornApparel: false);
            }
        }

        private static void CleanupThingHolder(ThingOwner thingOwner, bool removeWornApparel)
        {
            if (thingOwner == null)
            {
                return;
            }

            List<Thing> things = new List<Thing>();
            for (int i = 0; i < thingOwner.Count; i++)
            {
                things.Add(thingOwner[i]);
            }

            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing thing = things[i];
                if (thing == null)
                {
                    continue;
                }

                if (thing is Pawn pawn)
                {
                    CleanupPawn(pawn, removeWornApparel);
                    continue;
                }

                if (FilterThing(thing))
                {
                    thingOwner.Remove(thing);
                    if (!thing.Destroyed)
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
            }
        }
    }

    public class AdultContentCleanupGameComponent : GameComponent
    {
        public AdultContentCleanupGameComponent(Game game)
        {
        }

        public AdultContentCleanupGameComponent()
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            if (!AdultContentUtility.AdultContentEnabled)
            {
                AdultContentUtility.CleanupAllAdultContent(removeWornApparel: true);
            }
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            if (!AdultContentUtility.AdultContentEnabled)
            {
                AdultContentUtility.CleanupAllAdultContent(removeWornApparel: true);
            }
        }
    }

    [HarmonyPatch(typeof(ThingSetMaker_TraderStock), "Generate")]
    public static class Patch_ThingSetMaker_TraderStock_Generate
    {
        public static void Postfix(List<Thing> outThings)
        {
            if (AdultContentUtility.AdultContentEnabled || outThings == null)
            {
                return;
            }

            for (int i = outThings.Count - 1; i >= 0; i--)
            {
                Thing thing = outThings[i];
                if (AdultContentUtility.FilterThing(thing))
                {
                    outThings.RemoveAt(i);
                    if (thing != null && !thing.Destroyed)
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
            }
        }
    }
}
