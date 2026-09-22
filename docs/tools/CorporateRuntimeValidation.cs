// 仅由 EnableCorporateValidation 构建启用；所有操作限定为显式指定的独立测试存档。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Mugirl
{
    internal sealed class CorporateRuntimeValidation
    {
        private int phase;
        private int persistedOrder;
        private int persistedStockId;
        private int persistedProtection;
        private int[] persistedGiftIds;
        private readonly List<string> results = new List<string>();
        private bool running;
        private bool FusionOnly => GenCommandLine.CommandLineArgPassed("mugirlCorporateFusionChecks");
        private bool FusionFloorsOnly => GenCommandLine.CommandLineArgPassed("mugirlCorporateFusionFloorChecks");
        private Dictionary<IntVec3, string> persistedFloors;
        private int persistedFloorMapId;
        private int persistedFloorMissionId;

        public CorporateRuntimeValidation(Game game) { }

        private bool Enabled
        {
            get
            {
                if (!GenCommandLine.CommandLineArgPassed("mugirlCorporateChecks")) return false;
                string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(CorporateNetwork).Assembly.Location), "..", "..", "TMP"))
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(GenFilePaths.SaveDataFolderPath).StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Test-only roundtrip expectations stay in this process, outside game data.
        internal bool AwaitingReload => phase == 1 && running;

        internal void ResumeAfterReload() { running = false; }

        internal void Update()
        {
            if (!Enabled || running || Current.ProgramState != ProgramState.Playing || Find.CurrentMap?.mapPawns.FreeColonistsSpawned.Count < 1) return;
            running = true;
            Application.runInBackground = true;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            try
            {
                if (FusionFloorsOnly)
                {
                    if (phase == 0) RunFusionFloorChecks();
                    else if (phase == 1)
                    {
                        Map restoredMap = Find.Maps.FirstOrDefault(map => map.uniqueID == persistedFloorMapId);
                        Check("Research mission map survives full save/load", restoredMap != null);
                        if (restoredMap != null) VerifyFusionFloors(restoredMap, "after full save/load");
                        CorporateMission mission = CorporateNetwork.Current.FindMission(persistedFloorMissionId);
                        Check("Research team and active mission survive full save/load", mission?.spawned == true
                            && mission.state == CorporateMissionState.Active && mission.targets.Count == 4
                            && mission.targets.All(pawn => pawn.Spawned && pawn.Map == restoredMap));
                        Finish();
                    }
                }
                else if (phase == 0) QueueAsyncTailInitializationCheck();
                else if (phase == 1) VerifyReload();
            }
            catch (Exception exception)
            {
                Check("runtime exception: " + exception, false);
                Finish();
            }
        }

        private void Check(string label, bool passed)
        {
            string line = (passed ? "PASS " : "FAIL ") + label;
            results.Add(line);
            Directory.CreateDirectory(GenFilePaths.SaveDataFolderPath);
            File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "corporate-checks.txt"), line + Environment.NewLine);
            MugirlLog.DiagnosticMessage("[CorporateValidation] " + line);
        }

        private void RunFusionFloorChecks()
        {
            Map map = Find.CurrentMap;
            PrefabDef prefab = CorporateQuestDefOf.Mugirl_CorporateFusionResearchSite;
            var floors = prefab.GetTerrain().ToList();
            Check("Research prefab loads a nonempty floor plan", floors.Count > 0);
            if (floors.Count == 0) throw new InvalidOperationException("Research prefab has no terrain data.");
            Check("Research floor plan contains no overlapping cells", floors.Select(entry => entry.cell).Distinct().Count() == floors.Count);
            Check("Research floor plan stays inside the prefab", floors.All(entry => entry.cell.x >= 0 && entry.cell.x < prefab.size.x
                && entry.cell.z >= 0 && entry.cell.z < prefab.size.z));
            Check("Research floor plan has four intended materials", floors.Select(entry => entry.data.def.defName).Distinct()
                .OrderBy(name => name).SequenceEqual(new[] { "Concrete", "PavedTile", "SterileTile", "WoodPlankFloor" }));
            IntVec3 root = PrefabUtility.GetRoot(prefab, map.Center, Rot4.North);
            persistedFloors = floors.ToDictionary(entry => root + entry.cell, entry => entry.data.def.defName);
            CellRect siteRect = GenAdj.OccupiedRect(map.Center, Rot4.North, prefab.size);
            // 夹具仅运行于隔离游戏；将殖民者移到清场范围之外，避免建筑挤走验证入口 Pawn。
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned.ToList())
            {
                IntVec3 destination = CellFinder.RandomClosewalkCellNear(new IntVec3(8, 0, 8), map, 6);
                pawn.DeSpawn();
                GenSpawn.Spawn(pawn, destination, map);
            }
            foreach (string terrainName in new[] { "Soil", "ThinIce", "WaterDeep", "Mud", "Bridge", "RockAndGeyser", "ForcedFallback" })
            {
                TerrainDef terrain = DefDatabase<TerrainDef>.GetNamed(terrainName == "ForcedFallback" || terrainName == "RockAndGeyser" ? "Soil" : terrainName);
                foreach (IntVec3 cell in siteRect)
                {
                    CorporateNetwork.ClearFusionResearchCell(map, cell, removePlants: true);
                    if (map.terrainGrid.FoundationAt(cell) != null) map.terrainGrid.RemoveFoundation(cell, doLeavings: false);
                    map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                    map.terrainGrid.SetTerrain(cell, terrain);
                }
                if (terrainName == "RockAndGeyser")
                {
                    ThingDef rock = DefDatabase<ThingDef>.GetNamed("Granite");
                    foreach (IntVec3 cell in siteRect.ExpandedBy(8))
                    {
                        CorporateNetwork.ClearFusionResearchCell(map, cell, removePlants: true);
                        GenSpawn.Spawn(ThingMaker.MakeThing(rock), cell, map);
                        map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThick);
                    }
                    GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("SteamGeyser")), map.Center, map);
                }
                Harmony rejection = new Harmony("Mugirl.Validation.FusionFloorFallback");
                MethodInfo canSpawn = AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.CanSpawnAt));
                try
                {
                    // 模拟第三方放置条件拒绝发电机：走真实整地与原版静默跳过后的补建分支。
                    if (terrainName == "ForcedFallback")
                        rejection.Patch(canSpawn, prefix: new HarmonyMethod(typeof(CorporateRuntimeValidation), nameof(RejectResearchGenerator)));
                    CorporateNetwork.DevSpawnFusionResearchSite(map);
                }
                finally { rejection.Unpatch(canSpawn, HarmonyPatchType.Prefix, rejection.Id); }
                VerifyFusionFloors(map, "generated over " + terrainName);
                TerrainDef expectedPasture = terrain.temporary || terrain.passability == Traversability.Impassable ? TerrainDefOf.Soil : terrain;
                IntVec3 pasture = root + new IntVec3(2, 0, 20);
                // 山岩生成会铺设天然石地面；清场应保留可通行石地面，无需强改回土壤。
                Check("Research pasture remains safe over " + terrainName + ": " + pasture.GetTerrain(map).defName,
                    pasture.Walkable(map) && (terrainName == "RockAndGeyser"
                        ? !pasture.GetTerrain(map).temporary : pasture.GetTerrain(map) == expectedPasture));
                Check("Research generators spawn with fuel over " + terrainName, map.listerThings.AllThings
                    .Where(thing => thing.def.defName == "Mugirl_MilkPoweredGenerator").Count() == 6
                    && map.listerThings.AllThings.Where(thing => thing.def.defName == "Mugirl_MilkPoweredGenerator")
                        .All(thing => thing.TryGetComp<CompRefuelable>()?.IsFull == true));
            }
            RunFusionFloorMissionCheck(map);
        }

        private static bool RejectResearchGenerator(ThingDef thingDef, ref bool __result)
        {
            if (thingDef.defName != "Mugirl_MilkPoweredGenerator") return true;
            __result = false;
            return false;
        }

        private static bool RejectPreferredResearchTile(Predicate<PlanetTile> validator, ref PlanetTile tile, ref bool __result)
        {
            if (validator == null) return true;
            tile = PlanetTile.Invalid;
            __result = false;
            return false;
        }

        private void RunFusionFloorMissionCheck(Map homeMap)
        {
            bool preferredExists = TileFinder.TryFindNewSiteTile(out PlanetTile preferred, homeMap.Tile, 2, 10,
                allowCaravans: false, selectLandmarkChance: 0f, validator: CorporateNetwork.IsPreferredFusionResearchTile);
            bool found = CorporateNetwork.TryFindFusionResearchSiteTile(homeMap.Tile, out PlanetTile selected);
            Check("Research selection prefers suitable terrain when available: preferred=" + preferredExists,
                found && (!preferredExists || CorporateNetwork.IsPreferredFusionResearchTile(selected)));
            CorporateNetwork network = CorporateNetwork.Current;
            typeof(CorporateIntroduction).GetField("completed", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(CorporateIntroduction.Current, true);
            network.CorporateFaction.TryAffectGoodwillWith(Faction.OfPlayer, 100 - network.CorporateFaction.PlayerGoodwill, false, false);
            CorporateMission mission = new CorporateMission { id = network.Missions.Select(item => item.id).DefaultIfEmpty().Max() + 1,
                kind = CorporateMissionKind.Fusion, state = CorporateMissionState.Available,
                acceptByTick = CorporateNetwork.Now + 60000, duration = 600000 };
            ((List<CorporateMission>)network.Missions).Add(mission);
            Harmony rejection = new Harmony("Mugirl.Validation.FusionPreferredTileFallback");
            MethodInfo findTile = typeof(TileFinder).GetMethods().Single(method => method.Name == nameof(TileFinder.TryFindNewSiteTile)
                && method.GetParameters()[1].ParameterType == typeof(PlanetTile));
            try
            {
                rejection.Patch(findTile, prefix: new HarmonyMethod(typeof(CorporateRuntimeValidation), nameof(RejectPreferredResearchTile)));
                bool accepted = network.AcceptMission(mission, new CorporateTradeContext(homeMap), out string reason);
                Check("Research mission accepts without any preferred terrain: " + reason, accepted && mission.site != null);
                if (!accepted) throw new InvalidOperationException(reason);
            }
            finally { rejection.Unpatch(findTile, HarmonyPatchType.Prefix, rejection.Id); }
            persistedFloorMissionId = mission.id;
            LongEventHandler.QueueLongEvent(() =>
            {
                try
                {
                    Map siteMap = GetOrGenerateMapUtility.GetOrGenerateMap(mission.site.Tile, new IntVec3(125, 1, 125), mission.site.def);
                    Check("Research real PostMapGenerate creates a complete active team", mission.spawned
                        && mission.state == CorporateMissionState.Active && mission.targets.Count == 4
                        && mission.targets.All(pawn => pawn.Spawned && pawn.Map == siteMap));
                    persistedFloorMapId = siteMap.uniqueID;
                    PrefabDef prefab = CorporateQuestDefOf.Mugirl_CorporateFusionResearchSite;
                    IntVec3 root = PrefabUtility.GetRoot(prefab, siteMap.Center, Rot4.North);
                    persistedFloors = prefab.GetTerrain().ToDictionary(entry => root + entry.cell, entry => entry.data.def.defName);
                    VerifyFusionFloors(siteMap, "on actual mission map");
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        try
                        {
                            phase = 1;
                            GameDataSaveLoader.SaveGame("CorporateFusionFloorsRoundtrip");
                            Check("Research floor full game save produced", File.Exists(GenFilePaths.FilePathForSavedGame("CorporateFusionFloorsRoundtrip")));
                            GameDataSaveLoader.LoadGame("CorporateFusionFloorsRoundtrip");
                        }
                        catch (Exception exception) { Check("floor save exception: " + exception, false); Finish(); }
                    });
                }
                catch (Exception exception) { Check("floor mission exception: " + exception, false); Finish(); }
            }, "GeneratingMap", false, null);
        }

        private void VerifyFusionFloors(Map map, string stage)
        {
            int matched = persistedFloors.Count(entry => entry.Key.GetTerrain(map).defName == entry.Value);
            Check("Research floors " + stage + ": " + matched + "/" + persistedFloors.Count, matched == persistedFloors.Count);
            PrefabDef prefab = CorporateQuestDefOf.Mugirl_CorporateFusionResearchSite;
            var expected = PrefabUtility.GetThings(prefab, map.Center, Rot4.North).ToList();
            int buildings = expected.Count(entry => entry.Item2.GetThingList(map)
                .Any(thing => thing.Position == entry.Item2 && thing.def == entry.Item1.def));
            Check("Research prefab contents " + stage + ": " + buildings + "/" + expected.Count, buildings == expected.Count);
            IntVec3 start = PrefabUtility.GetRoot(prefab, map.Center, Rot4.North) + new IntVec3(31, 0, 20);
            HashSet<IntVec3> visited = new HashSet<IntVec3> { start };
            Queue<IntVec3> pending = new Queue<IntVec3>();
            pending.Enqueue(start);
            int edges = 0;
            IntVec3[] directions = { new IntVec3(1, 0, 0), new IntVec3(-1, 0, 0), new IntVec3(0, 0, 1), new IntVec3(0, 0, -1) };
            while (pending.Count > 0 && edges != 15)
            {
                IntVec3 cell = pending.Dequeue();
                if (cell.x == 0) edges |= 1;
                if (cell.x == map.Size.x - 1) edges |= 2;
                if (cell.z == 0) edges |= 4;
                if (cell.z == map.Size.z - 1) edges |= 8;
                foreach (IntVec3 direction in directions)
                {
                    IntVec3 next = cell + direction;
                    if (next.InBounds(map) && next.Walkable(map) && visited.Add(next)) pending.Enqueue(next);
                }
            }
            Check("Research courtyard reaches all four map edges " + stage, start.Walkable(map) && edges == 15);
        }

        private void QueueAsyncTailInitializationCheck()
        {
            Map map = Find.CurrentMap;
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(map.mapPawns.FreeColonistsSpawned.First().Position, map, 8);
            int mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            int spawnThreadId = mainThreadId;
            Pawn fixture = null;
            Comp_SegmentedMechTail tail = null;
            Exception spawnException = null;
            bool deferredOnWorker = false;
            FieldInfo readyField = typeof(Comp_SegmentedMechTail).GetField("resourcesReady", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo segmentField = typeof(Comp_SegmentedMechTail).GetField("segmentMesh", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo tipField = typeof(Comp_SegmentedMechTail).GetField("tipMesh", BindingFlags.Instance | BindingFlags.NonPublic);
            LongEventHandler.QueueLongEvent(() =>
            {
                try
                {
                    spawnThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                    fixture = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        DefDatabase<PawnKindDef>.GetNamed("Mugirl_MechanoidBulldog"),
                        faction: Faction.OfPlayer, forceGenerateNewPawn: true, allowDead: false,
                        allowDowned: false, canGeneratePawnRelations: false));
                    GenSpawn.Spawn(fixture, cell, map);
                    tail = fixture.TryGetComp<Comp_SegmentedMechTail>();
                    deferredOnWorker = tail != null && !(bool)readyField.GetValue(tail)
                        && ReferenceEquals(segmentField.GetValue(tail), null)
                        && ReferenceEquals(tipField.GetValue(tail), null);
                }
                catch (Exception exception) { spawnException = exception; }
                // PostSpawnSetup queued its own resource callback first. This callback checks
                // the finished resources before the normal corporate and site/save sequence.
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    try
                    {
                        Check("Segmented-tail fixture spawns in a real asynchronous long event",
                            spawnException == null && fixture != null && fixture.Spawned
                            && spawnThreadId != mainThreadId);
                        Check("Asynchronous tail spawn defers all Unity mesh allocation", deferredOnWorker);
                        Check("Deferred tail initialization returns to the main thread and creates both meshes",
                            System.Threading.Thread.CurrentThread.ManagedThreadId == mainThreadId
                            && tail != null && (bool)readyField.GetValue(tail)
                            && segmentField.GetValue(tail) is Mesh && tipField.GetValue(tail) is Mesh);
                        if (spawnException != null) throw new InvalidOperationException("Asynchronous tail fixture failed", spawnException);
                        // Only remove this generated test pawn, on the main thread so its
                        // normal PostDestroy also releases the meshes before other fixtures.
                        if (fixture != null && !fixture.Destroyed) fixture.Destroy(DestroyMode.Vanish);
                        RunFirstStage();
                    }
                    catch (Exception exception)
                    {
                        Check("tail/first-stage runtime exception: " + exception, false);
                        Finish();
                    }
                });
            }, "GeneratingMap", true, null);
        }

        private void RunFirstStage()
        {
            bool expectedIdeology = !GenCommandLine.CommandLineArgPassed("mugirlCorporateNoIdeology");
            Check("actual Ideology activation matches requested fixture (active=" + ModsConfig.IdeologyActive + ")", ModsConfig.IdeologyActive == expectedIdeology);
            if (ModsConfig.IdeologyActive != expectedIdeology) throw new InvalidOperationException("Actual DLC configuration does not match this test run.");
            Map map = Find.CurrentMap;
            CorporateNetwork network = CorporateNetwork.Current;
            Check("new game starts with commercial network locked", !network.Unlocked);
            // 主线真实对白与解锁另由视觉驱动走完整路线；业务测试设置独立已开户夹具。
            typeof(CorporateIntroduction).GetField("completed", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(CorporateIntroduction.Current, true);
            Check("corporate faction is present", network.CorporateFaction != null);
            if (network.CorporateFaction == null) throw new InvalidOperationException("Corporate faction missing");
            Check("corporate faction permits ordinary diplomacy", !network.CorporateFaction.def.permanentEnemy);
            network.CorporateFaction.TryAffectGoodwillWith(Faction.OfPlayer, -network.CorporateFaction.PlayerGoodwill, false, false);
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned) pawn.jobs.StopAll();
            // Quicktest may put the map centre inside a mountain or a small enclosed room.
            // Choose a real open trade area before creating the isolated resource fixture.
            IntVec3 tradeCenter = GenRadial.RadialCellsAround(map.mapPawns.FreeColonistsSpawned.First().Position, 40f, true)
                .Where(c => c.InBounds(map) && c.Standable(map)).InRandomOrder().Take(40)
                .OrderByDescending(c => Building_OrbitalTradeBeacon.TradeableCellsAround(c, map).Count).First();
            Building_OrbitalTradeBeacon beacon = (Building_OrbitalTradeBeacon)ThingMaker.MakeThing(ThingDefOf.OrbitalTradeBeacon);
            beacon.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(beacon, tradeCenter, map);
            beacon.TryGetComp<CompPowerTrader>().PowerOn = true;
            List<IntVec3> cells = beacon.TradeableCells.Where(c => c.InBounds(map) && c.Standable(map) && c != tradeCenter).Take(200).ToList();
            foreach (IntVec3 cell in cells)
            {
                Thing money = ThingMaker.MakeThing(ThingDefOf.Silver);
                money.stackCount = ThingDefOf.Silver.stackLimit;
                GenSpawn.Spawn(money, cell, map);
            }
            CorporateTradeContext context = new CorporateTradeContext(map) { Negotiator = map.mapPawns.FreeColonistsSpawned.First() };
            Check("trade beacon exposes real silver", context.SilverCount >= 40000);
            if (context.SilverCount < 40000) throw new InvalidOperationException("Powered colony beacon must expose the fixture's silver; actual=" + context.SilverCount);
            network.EnsureWeeklyOffers();
            Check("weekly stock generated", network.Stock.Count == network.TradeSettings.weeklyStockCount);
            Check("daily board offers six contracts", network.Missions.Count(m => m.state == CorporateMissionState.Available) == 6);
            int[] ids = network.Stock.Select(s => s.id).ToArray();
            network.EnsureWeeklyOffers();
            Check("reopening does not reroll weekly stock", ids.SequenceEqual(network.Stock.Select(s => s.id)));
            Check("order catalogue includes advanced ordinary goods", network.OrderCatalog.Any(d => d.techLevel >= TechLevel.Spacer));
            if (FusionOnly)
            {
                // 谢礼存读档不依赖人员买卖信仰夹具或随机研究站地形。
                CorporateQuestRuntimeChecks.Run(map, network, context, Check);
                SaveForRoundtrip(network, context);
                return;
            }
            CorporateIntroRuntimeChecks.Run(map, network, context, Check);
            RunTradeChecks(map, context, network);
            CorporateFinanceRuntimeChecks.Run(map, network, context, Check);
            CorporateQuestRuntimeChecks.Run(map, network, context, Check);
            network.CorporateFaction.TryAffectGoodwillWith(Faction.OfPlayer, 100 - network.CorporateFaction.PlayerGoodwill, false, false);
            LongEventHandler.QueueLongEvent(() =>
            {
                try
                {
                    CorporateQuestRuntimeChecks.RunSites(map, network, context, Check);
                    // 地图生成的图形初始化与夹具清理先完成，再执行完整存档往返。
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        try { SaveForRoundtrip(network, context); }
                        catch (Exception exception) { Check("save runtime exception: " + exception, false); Finish(); }
                    });
                }
                catch (Exception exception)
                {
                    Check("site/save runtime exception: " + exception, false);
                    Finish();
                }
            }, "GeneratingMap", false, null);
        }

        private void SaveForRoundtrip(CorporateNetwork network, CorporateTradeContext context)
        {
            if (!FusionOnly) CorporateServicesRuntimeChecks.Run(context.Map, network, context, Check);
            // 保存一份仍在履行的真实订单，在完整存档往返后检查引用和金额。
            network.EnsureWeeklyOffers();
            network.CorporateFaction.TryAffectGoodwillWith(Faction.OfPlayer, 100 - network.CorporateFaction.PlayerGoodwill, false, false);
            bool placed = network.PlaceOrder(context, ThingDefOf.ComponentSpacer, null, QualityCategory.Normal, 1, out string reason);
            Check("persistence fixture order created: " + reason, placed);
            persistedOrder = network.Orders.Last().id;
            persistedStockId = network.Stock.First().id;
            // 保留未发出的谢礼跨完整存读档，确认真实物品与任务引用一并恢复。
            CorporateMission protection = new CorporateMission { id = network.Missions.Max(m => m.id) + 1,
                kind = CorporateMissionKind.Fusion, state = CorporateMissionState.Active, reward = 4000, goodwill = 10 };
            ((List<CorporateMission>)network.Missions).Add(protection);
            typeof(CorporateNetwork).GetMethod("ProtectResearchers", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(network, new object[] { protection });
            persistedProtection = protection.id;
            persistedGiftIds = protection.pendingGoods.Select(t => t.thingIDNumber).ToArray();
            Check("persistence fixture has pending protection gifts", protection.pendingGoods.Count > 0);
            phase = 1;
            GameDataSaveLoader.SaveGame("CorporateRoundtrip");
            Check("full game save produced", File.Exists(GenFilePaths.FilePathForSavedGame("CorporateRoundtrip")));
            GameDataSaveLoader.LoadGame("CorporateRoundtrip");
        }

        private void RunTradeChecks(Map map, CorporateTradeContext context, CorporateNetwork network)
        {
            string reason;
            CorporateStock offer = network.Stock.First(s => s.count > 0);
            int oldCount = offer.count;
            int oldSilver = context.SilverCount;
            bool success = network.BuyStock(context, offer, 1, out reason);
            Check("stock purchase succeeds: " + reason, success);
            Check("stock purchase consumes exact silver and finite inventory", context.SilverCount == oldSilver - offer.unitPrice && offer.count == oldCount - 1);
            Check("delivered receipt cannot be claimed again", !network.ClaimOrder(context, network.Orders.Last(), out reason));

            Thing milk = ThingMaker.MakeThing(Mugirl_DefOf.Mugirl_Milk);
            milk.stackCount = 20;
            GenSpawn.Spawn(milk, context.AvailableThings.First(t => t.def == ThingDefOf.Silver).Position, map);
            context.Invalidate();
            Check("milk receives premium quote", network.ProductQuote(milk, 10) > milk.MarketValue * 10);
            Check("partial product sale consumes the requested amount", network.SellProduct(context, milk, 10, out reason) && milk.stackCount == 10);
            Check("ordinary steel is not accepted in produce buyback", network.ProductFactor(ThingMaker.MakeThing(ThingDefOf.Steel)) == 0f);
            Thing wool = ThingMaker.MakeThing(CorporateTradeDefOf.Mugirl_Wool);
            Check("Mugirl wool receives the raw-material premium", network.ProductQuote(wool, 1) > wool.MarketValue);
            ThingDef apparelDef = network.OrderCatalog.First(d => d.IsApparel && d.MadeFromStuff
                && GenStuff.AllowedStuffsFor(d).Contains(wool.def));
            Thing apparel = CorporateNetwork.MakeProduct(apparelDef, wool.def, QualityCategory.Good);
            Check("real wool apparel is recognized from its material", network.ProductQuote(apparel, 1) > apparel.MarketValue);
            Thing meal = ThingMaker.MakeThing(ThingDefOf.MealSimple);
            Check("food without evidence of Mugirl milk is excluded", network.ProductFactor(meal) == 0f);
            meal.TryGetComp<CompIngredients>().ingredients.Add(Mugirl_DefOf.Mugirl_Milk);
            Check("food retaining the actual milk ingredient qualifies for product procurement", network.ProductQuote(meal, 1) > meal.MarketValue);
            meal.stackCount = 3;
            GenSpawn.Spawn(meal, context.AvailableThings.First(t => t.def == ThingDefOf.Silver).Position, map);
            context.Invalidate();
            Check("milk-food procurement accepts real Buyable meals and transfers only the requested quantity",
                network.SellProduct(context, meal, 1, out reason) && meal.stackCount == 2);
            wool.Destroy(); apparel.Destroy(); meal.Destroy();

            success = network.PlaceOrder(context, ThingDefOf.ComponentSpacer, null, QualityCategory.Normal, 2, out reason);
            Check("rare order accepted with escrowed goods: " + reason, success);
            CorporateOrder order = network.Orders.Last();
            Check("order starts in preparation and owns physical goods", order.state == CorporateOrderState.Preparing
                && order.goods.All(t => t.holdingOwner == network.Vault));
            Check("order cannot be claimed early", !network.ClaimOrder(context, order, out reason));
            Find.TickManager.DebugSetTicksGame(order.readyTick + 1);
            CorporateTradeContext denied = new CorporateTradeContext(map, () => false);
            Check("lost access preserves the paid order", !network.ClaimOrder(denied, order, out reason) && order.goods.Count > 0);
            Check("ready order delivers at a valid terminal", network.ClaimOrder(context, order, out reason) && order.state == CorporateOrderState.Completed);
            Check("second claim produces no duplicate goods", !network.ClaimOrder(context, order, out reason));

            network.PlaceOrder(context, ThingDefOf.Steel, null, QualityCategory.Normal, 5, out reason);
            CorporateOrder cancelled = network.Orders.Last();
            Check("first-day cancellation closes and clears escrow", network.CancelOrder(context, cancelled, out reason)
                && cancelled.state == CorporateOrderState.Cancelled && cancelled.goods.Count == 0);
            Check("cancelled order cannot refund twice", !network.CancelOrder(context, cancelled, out reason));

            network.PlaceOrder(context, ThingDefOf.Steel, null, QualityCategory.Normal, 5, out reason);
            CorporateOrder missing = network.Orders.Last();
            missing.def = null;
            Check("missing def produces a recoverable refund", network.ClaimOrder(context, missing, out reason) && missing.state == CorporateOrderState.Refunded);
            Check("missing-def refund cannot be repeated", !network.ClaimOrder(context, missing, out reason));
        }

        private void VerifyReload()
        {
            CorporateNetwork network = CorporateNetwork.Current;
            Check("commercial unlock survives full save/load", network.Unlocked);
            CorporateOrder order = network.Orders.FirstOrDefault(o => o.id == persistedOrder);
            Check("pending order survives full save/load", order != null && order.state == CorporateOrderState.Preparing);
            Check("order objects have a single restored vault owner", order != null && order.goods.Count > 0 && order.goods.All(t => t != null && t.holdingOwner == network.Vault));
            Check("weekly stock is not rerolled on load", network.Stock.Any(s => s.id == persistedStockId));
            Check("escrow inventory has no duplicate references", network.Vault.Distinct().Count() == network.Vault.Count);
            // 读档后的首个 Game.UpdatePlay 可能已推进业务 tick；按原物品 ID 检查 Vault 或运输舱中的真实货物。
            CorporateMission protection = network.Missions.FirstOrDefault(m => m.id == persistedProtection);
            List<Thing> restoredGifts = network.Vault.ToList();
            foreach (Map map in Find.Maps)
            {
                List<Thing> mapThings = new List<Thing>();
                ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForGroup(ThingRequestGroup.Everything), mapThings);
                restoredGifts.AddRange(mapThings);
            }
            restoredGifts = restoredGifts.Distinct().Where(t => persistedGiftIds.Contains(t.thingIDNumber)).ToList();
            Check("protection result and exact gifts survive full save/load: pending=" + protection?.pendingGoods.Count
                + ", restored=" + restoredGifts.Count + ", expected=" + persistedGiftIds.Length, protection != null
                && protection.choice == CorporateResearchChoice.Protect && protection.state == CorporateMissionState.Completed
                && protection.reward == 0 && restoredGifts.Count == persistedGiftIds.Length && restoredGifts.Count > 0
                && restoredGifts.All(t => !t.Destroyed && (t.holdingOwner == network.Vault
                    ? protection.pendingGoods.Contains(t) : t.MapHeld?.IsPlayerHome == true))
                && CorporateNetwork.MissionConfig.fusionProtectionRewards.All(reward =>
                    restoredGifts.Where(t => t.def == reward.thingDef).Sum(t => t.stackCount) == reward.count));
            if (protection != null)
            {
                typeof(CorporateNetwork).GetField("nextMissionCheck", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(network, 0);
                typeof(CorporateNetwork).GetMethod("QuestsTick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(network, null);
                Check("restored protection gifts dispatch once", restoredGifts.Count == persistedGiftIds.Length && restoredGifts.Count > 0
                    && protection.pendingGoods.Count == 0
                    && restoredGifts.All(t => !t.Destroyed && t.holdingOwner != network.Vault && t.MapHeld?.IsPlayerHome == true));
                int vaultCount = network.Vault.Count;
                typeof(CorporateNetwork).GetMethod("ProtectResearchers", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(network, new object[] { protection });
                Check("completed protection after reload cannot generate another gift", protection.pendingGoods.Count == 0 && network.Vault.Count == vaultCount);
            }
            if (!FusionOnly) CorporateServicesRuntimeChecks.VerifyReload(network, Check);
            Check("gameplay DLL has expected Harmony patches", MugirlBootstrap.PatchedClassNames.Contains(typeof(Harmony_CorporateCommsConsole).FullName)
                && MugirlBootstrap.PatchedClassNames.Contains(typeof(CorporateDebt_HostileTo_Patch).FullName));
            Finish();
        }

        private void Finish()
        {
            phase = 2;
            File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "checks-complete.txt"), DateTime.UtcNow.ToString("O"));
            Application.Quit();
        }
    }
}
