// 临时独立探针，限当前模组 TMP 下的隔离配置及显式命令行开关；不编入正式 DLL。
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;
using Mugirl;
using RimWorld;
using UnityEngine;
using Verse;

namespace MugirlPricingProbe
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        private static string output;
        private static Game savedGame;
        private static int phase, passed, failed, orderId, paid, due, fee, oldId, oldDue;
        private static float shotAt;
        private static Window_CorporateComms window;

        static Bootstrap()
        {
            if (!GenCommandLine.CommandLineArgPassed("mugirlPricingProbe")) return;
            string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(CorporateNetwork).Assembly.Location), "../..", "TMP"));
            output = Path.GetFullPath(GenFilePaths.SaveDataFolderPath);
            if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            var harmony = new Harmony("mugirl.pricing.runtime.probe");
            harmony.Patch(AccessTools.Method(typeof(Game), "UpdatePlay"), postfix: new HarmonyMethod(typeof(Bootstrap), "Update"));
            File.WriteAllText(Path.Combine(output, "pricing-progress.txt"), "Ready for isolated quicktest");
            Application.runInBackground = true;
        }

        private static void Check(string label, bool success)
        {
            if (success) passed++; else failed++;
            File.AppendAllText(Path.Combine(output, "pricing-checks.txt"), (success ? "PASS " : "FAIL ") + label + Environment.NewLine);
        }

        private static void Update()
        {
            if (phase >= 5 || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting
                || Find.CurrentMap?.mapPawns.FreeColonistsSpawned.Count < 1) return;
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                if (phase == 0) Run();
                else if (phase == 1 && !ReferenceEquals(Current.Game, savedGame)) Reloaded();
                else if (phase == 2 && Time.realtimeSinceStartup > shotAt) ShowOrder();
                else if (phase == 3 && Time.realtimeSinceStartup > shotAt)
                { ScreenCapture.CaptureScreenshot(Path.Combine(output, "orders.png")); phase = 4; shotAt = Time.realtimeSinceStartup + 2f; }
                else if (phase == 4 && Time.realtimeSinceStartup > shotAt) Finish();
            }
            catch (Exception ex) { Check("runtime exception: " + ex, false); Finish(); }
        }

        private static CorporateTradeContext Context()
        {
            Map map = Find.CurrentMap;
            return new CorporateTradeContext(map) { Negotiator = map.mapPawns.FreeColonistsSpawned.First() };
        }

        private static void Run()
        {
            phase = 1;
            File.WriteAllText(Path.Combine(output, "pricing-progress.txt"), "Testing quotes and contracts");
            Map map = Find.CurrentMap;
            CorporateNetwork network = CorporateNetwork.Current;
            AccessTools.Field(typeof(CorporateIntroduction), "completed").SetValue(CorporateIntroduction.Current, true);
            if (network.CorporateFaction == null) throw new InvalidOperationException("Quicktest corporate faction missing");
            network.CorporateFaction.TryAffectGoodwillWith(Faction.OfPlayer, 100 - network.CorporateFaction.PlayerGoodwill, false, false);
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned) pawn.jobs.StopAll();
            IntVec3 center = GenRadial.RadialCellsAround(map.mapPawns.FreeColonistsSpawned.First().Position, 40f, true)
                .Where(c => c.InBounds(map) && c.Standable(map)).Take(80)
                .OrderByDescending(c => Building_OrbitalTradeBeacon.TradeableCellsAround(c, map).Count).First();
            var beacon = (Building_OrbitalTradeBeacon)ThingMaker.MakeThing(ThingDefOf.OrbitalTradeBeacon);
            beacon.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(beacon, center, map);
            beacon.TryGetComp<CompPowerTrader>().PowerOn = true;
            foreach (IntVec3 cell in beacon.TradeableCells.Where(c => c.InBounds(map) && c.Standable(map) && c != center).Take(100))
            { Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver); silver.stackCount = 500; GenSpawn.Spawn(silver, cell, map); }
            CorporateTradeContext context = Context();
            Check("Real powered beacon exposes test silver", context.SilverCount >= 10000);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            CorporatePricingChecks.RunQuotes(network, Check);
            File.WriteAllText(Path.Combine(output, "pricing-analysis.txt"), "Quote suite ms=" + timer.ElapsedMilliseconds
                + "\nCatalogue=" + network.OrderCatalog.Count + "\nAutoSupply=" + network.OrderCatalog.Count(network.CanAutomaticallySupply)
                + "\nRestricted=\n" + string.Join("\n", network.OrderCatalog.Where(d => !network.CanAutomaticallySupply(d)).Select(d => d.defName)));
            CorporatePricingChecks.RunContract(network, context, Check);
            if (!network.PlaceOrder(context, ThingDefOf.Steel, null, QualityCategory.Normal, 20, out string reason, CorporateOrderSpeed.Priority))
                throw new InvalidOperationException(reason);
            CorporateOrder order = network.Orders.Last();
            orderId = order.id; paid = order.paid; due = order.readyTick; fee = order.expediteFee;
            if (!network.PlaceOrder(context, ThingDefOf.Steel, null, QualityCategory.Normal, 5, out reason)) throw new InvalidOperationException(reason);
            oldId = network.Orders.Last().id; oldDue = network.Orders.Last().readyTick;
            savedGame = Current.Game;
            GameDataSaveLoader.SaveGame("CorporatePricingRoundtrip");
            string path = GenFilePaths.FilePathForSavedGame("CorporatePricingRoundtrip");
            XDocument save = XDocument.Load(path);
            // 移除普通订单的新字段，验证旧档缺字段时的默认值与原交期。
            foreach (XElement node in save.Descendants("speed").Where(e => e.Parent.Element("id")?.Value == oldId.ToString()).ToList()) node.Remove();
            foreach (XElement node in save.Descendants("expediteFee").Where(e => e.Parent.Element("id")?.Value == oldId.ToString()).ToList()) node.Remove();
            save.Save(path);
            File.WriteAllText(Path.Combine(output, "pricing-progress.txt"), "Full save/load queued");
            GameDataSaveLoader.LoadGame("CorporatePricingRoundtrip");
        }

        private static void Reloaded()
        {
            CorporateNetwork network = CorporateNetwork.Current;
            CorporateOrder order = network.Orders.Single(o => o.id == orderId);
            Check("Full save/load preserves priority payment, fee and arrival", order.paid == paid && order.expediteFee == fee
                && order.readyTick == due && order.speed == CorporateOrderSpeed.Priority && order.goods.Count > 0);
            CorporateOrder old = network.Orders.Single(o => o.id == oldId);
            Check("Old contract without new fields preserves arrival and defaults to standard", old.speed == CorporateOrderSpeed.Standard
                && old.expediteFee == 0 && old.readyTick == oldDue);
            AccessTools.Field(typeof(CorporateNetwork), "tradeTurnover").SetValue(network, 400000L);
            Check("Reloaded contract is not repriced by customer upgrade", order.paid == paid && order.expediteFee == fee && order.readyTick == due);
            var restoredContext = Context();
            order.readyTick = CorporateNetwork.Now;
            Check("Completed expedited preparation cannot be cancelled", !network.CancelOrder(restoredContext, order, out _));
            Check("Ready expedited order can be collected", network.ClaimOrder(restoredContext, order, out _));
            Check("Collected order cannot be collected twice", !network.ClaimOrder(restoredContext, order, out _));
            window = new Window_CorporateComms(restoredContext);
            Find.WindowStack.Add(window);
            AccessTools.Field(typeof(Window_CorporateComms), "selectedPage").SetValue(window, 2);
            AccessTools.Field(typeof(Window_CorporateComms), "requestedPage").SetValue(window, 2);
            phase = 2; shotAt = Time.realtimeSinceStartup + 5f;
        }

        private static void ShowOrder()
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Meat_Twisted") ?? ThingDefOf.Steel;
            AccessTools.Field(typeof(Window_CorporateComms), "orderDef").SetValue(window, def);
            AccessTools.Field(typeof(Window_CorporateComms), "orderQuantity").SetValue(window, 100);
            AccessTools.Field(typeof(Window_CorporateComms), "orderQuantityText").SetValue(window, "100");
            AccessTools.Field(typeof(Window_CorporateComms), "orderSpeed").SetValue(window, CorporateOrderSpeed.Priority);
            AccessTools.Method(typeof(Window_CorporateComms), "RefreshOrderPreview").Invoke(window, null);
            phase = 3; shotAt = Time.realtimeSinceStartup + 3f;
        }

        private static void Finish()
        {
            phase = 5;
            File.WriteAllText(Path.Combine(output, "pricing-complete.txt"), "PASS=" + passed + " FAIL=" + failed);
            Application.Quit();
        }
    }
}
