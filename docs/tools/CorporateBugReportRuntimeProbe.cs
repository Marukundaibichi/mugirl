// 仅限显式命令行开关与本模组 TMP 内的隔离存档；单独编译，不进入正式 DLL。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mugirl;
using RimWorld;
using UnityEngine;
using Verse;

namespace MugirlBugReportProbe
{
    public class DisplayOnly : ThingComp
    {
        public override void PostDraw() { }
        public override string CompInspectStringExtra() => null;
        private void PostHelper() { }
    }
    public class Ticking : DisplayOnly { public override void CompTick() { } }
    public sealed class InheritedTicking : Ticking { }
    public sealed class UnknownUse : CompUseEffect { public override void DoEffect(Pawn usedBy) { } }
    public sealed class Initializing : DisplayOnly { public override void Initialize(CompProperties props) { base.Initialize(props); } }

    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        private static string output;
        private static int phase, passed, failed, oldOrderId, oldMissionId, detailDraws;
        private static bool checkedWideLayout, checkedCompactLayout;
        private static float nextAt;
        private static Game savedGame;
        private static Building_OrbitalTradeBeacon beacon;
        private static Window_CorporateComms window;
        private static ThingDef limitedStack;
        private static readonly Assembly mod = typeof(CorporateNetwork).Assembly;

        static Bootstrap()
        {
            if (!GenCommandLine.CommandLineArgPassed("mugirlBugReportProbe")) return;
            string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(mod.Location), "../..", "TMP"));
            output = Path.GetFullPath(GenFilePaths.SaveDataFolderPath);
            if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            var harmony = new Harmony("mugirl.bugreport.runtime.probe");
            harmony.Patch(AccessTools.Method(typeof(Game), "UpdatePlay"),
                postfix: new HarmonyMethod(typeof(Bootstrap), nameof(Update)));
            harmony.Patch(AccessTools.Method(typeof(Window_CorporateComms), "DrawOrderDetails"),
                postfix: new HarmonyMethod(typeof(Bootstrap), nameof(InspectLayout)));
            Application.runInBackground = true;
            File.WriteAllText(Path.Combine(output, "bug-progress.txt"), "Waiting for isolated quicktest");
        }

        private static void Check(string name, bool ok)
        {
            if (ok) passed++; else failed++;
            File.AppendAllText(Path.Combine(output, "bug-checks.txt"), (ok ? "PASS " : "FAIL ") + name + Environment.NewLine);
        }

        private static CorporateTradeContext Context() => new CorporateTradeContext(Find.CurrentMap)
        { Negotiator = Find.CurrentMap.mapPawns.FreeColonistsSpawned.First() };

        private static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
        private static T Get<T>(object target, string field) => (T)AccessTools.Field(target.GetType(), field).GetValue(target);
        private static void ResetAnalysis(CorporateNetwork network) => Set(network, "supplyAnalysis", null);

        private static void Update()
        {
            if (phase >= 8 || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting
                || Find.CurrentMap?.mapPawns.FreeColonistsSpawned.Count < 1) return;
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                if (phase == 0) Run();
                else if (phase == 1 && !ReferenceEquals(Current.Game, savedGame)) Reloaded();
                else if (phase == 2 && Time.realtimeSinceStartup > nextAt)
                {
                    Check("Order UI clamps restored input (quantity=" + Get<int>(window, "orderQuantity")
                        + ", text=" + Get<string>(window, "orderQuantityText") + ", drawCalls=" + detailDraws + ")", Get<int>(window, "orderQuantity") == 10
                        && Get<string>(window, "orderQuantityText") == "10");
                    ScreenCapture.CaptureScreenshot(Path.Combine(output, "order-wide.png"));
                    phase = 3; nextAt = Time.realtimeSinceStartup + 2f;
                }
                else if (phase == 3 && Time.realtimeSinceStartup > nextAt)
                {
                    window.Close(); Screen.SetResolution(760, 700, false);
                    phase = 4; nextAt = Time.realtimeSinceStartup + 3f;
                }
                else if (phase == 4 && Time.realtimeSinceStartup > nextAt)
                {
                    ShowOrder(); phase = 5; nextAt = Time.realtimeSinceStartup + 4f;
                }
                else if (phase == 5 && Time.realtimeSinceStartup > nextAt)
                {
                    Check("Compact order UI clamps quantity", Get<int>(window, "orderQuantity") == 10);
                    ScreenCapture.CaptureScreenshot(Path.Combine(output, "order-compact.png"));
                    phase = 6; nextAt = Time.realtimeSinceStartup + 2f;
                }
                else if (phase == 6 && Time.realtimeSinceStartup > nextAt)
                {
                    Set(window, "selectedPage", 5); Set(window, "requestedPage", 5);
                    phase = 7; nextAt = Time.realtimeSinceStartup + 4f;
                }
                else if (phase == 7 && Time.realtimeSinceStartup > nextAt)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(output, "missions-compact.png"));
                    Finish();
                }
            }
            catch (Exception ex) { Check("Runtime exception: " + ex, false); Finish(); }
        }

        private static void Run()
        {
            phase = 1;
            CorporateNetwork network = CorporateNetwork.Current;
            Set(CorporateIntroduction.Current, "completed", true);
            if (network.CorporateFaction == null) throw new InvalidOperationException("No corporate faction");
            network.CorporateFaction.TryAffectGoodwillWith(Faction.OfPlayer, 100 - network.CorporateFaction.PlayerGoodwill, false, false);
            Map map = Find.CurrentMap;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned) pawn.jobs.StopAll();
            IntVec3 center = GenRadial.RadialCellsAround(map.mapPawns.FreeColonistsSpawned.First().Position, 40f, true)
                .Where(c => c.InBounds(map) && c.Standable(map)).Take(80)
                .OrderByDescending(c => Building_OrbitalTradeBeacon.TradeableCellsAround(c, map).Count).First();
            beacon = (Building_OrbitalTradeBeacon)ThingMaker.MakeThing(ThingDefOf.OrbitalTradeBeacon);
            beacon.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(beacon, center, map);
            beacon.TryGetComp<CompPowerTrader>().PowerOn = true;
            foreach (IntVec3 cell in beacon.TradeableCells.Where(c => c.InBounds(map) && c.Standable(map) && c != center).Take(110))
            { Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver); silver.stackCount = 500; GenSpawn.Spawn(silver, cell, map); }
            CorporateTradeContext context = Context();
            Check("Powered beacon exposes silver", context.SilverCount >= 50000);
            CorporatePricingChecks.RunQuotes(network, Check);
            CorporatePricingChecks.RunContract(network, context, Check);
            RunCompatibility(network, context);
            RunLimits(network, context);
            RunFees();
            RunSubmission(network, context);
            RunDeposit(network, context);
            RunLocalization();
            SaveLegacyContracts(network);
        }

        private static ThingDef Fixture(string name, Type comp = null)
        {
            var def = new ThingDef { defName = "CorporateBugProbe_" + name, label = name, thingClass = typeof(ThingWithComps),
                category = ThingCategory.Item, tradeability = Tradeability.Sellable, stackLimit = 75,
                destroyable = true, techLevel = TechLevel.Industrial, comps = new List<CompProperties>(),
                statBases = new List<StatModifier> { new StatModifier { stat = StatDefOf.MarketValue, value = 1 },
                    new StatModifier { stat = StatDefOf.MaxHitPoints, value = 100 } } };
            if (comp != null) def.comps.Add(new CompProperties { compClass = comp });
            DefDatabase<ThingDef>.Add(def); return def;
        }

        private static void RunCompatibility(CorporateNetwork network, CorporateTradeContext context)
        {
            ThingDef knife = DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Knife");
            Check("Native knife is orderable", network.CanAutomaticallySupply(knife));
            CompProperties display = new CompProperties { compClass = typeof(DisplayOnly) };
            knife.comps.Add(display); ResetAnalysis(network);
            try
            {
                Check("Knife with display-only comp remains orderable", network.CanAutomaticallySupply(knife));
                bool placed = network.PlaceOrder(context, knife, ThingDefOf.Steel, QualityCategory.Normal, 1, out string reason);
                Check("Display-only knife reaches real paid contract: " + reason, placed);
                if (placed) network.CancelOrder(context, network.Orders.Last(), out _);
            }
            finally { knife.comps.Remove(display); ResetAnalysis(network); }
            foreach (Type type in new[] { typeof(Ticking), typeof(InheritedTicking), typeof(UnknownUse), typeof(Initializing) })
            {
                ThingDef def = Fixture(type.Name, type); ResetAnalysis(network);
                Check(type.Name + " remains restricted", !network.CanAutomaticallySupply(def));
                string reason = (string)AccessTools.Method(mod.GetType("Mugirl.CorporatePricingCompatibility"), "OpaqueBehaviorReason")
                    .Invoke(null, new object[] { def });
                Check(type.Name + " has component/method diagnostic", reason != null && reason.Contains(type.FullName) && reason.Contains(" -> "));
            }
        }

        private static void RunLimits(CorporateNetwork network, CorporateTradeContext context)
        {
            ThingDef def = Fixture("Threshold"); ResetAnalysis(network);
            MethodInfo rawPrice = AccessTools.Method(typeof(CorporateNetwork), "StandardOrderUnitPrice");
            MethodInfo rawLimit = AccessTools.Method(typeof(CorporateNetwork), "MaxOrderQuantity", new[] { typeof(ThingDef), typeof(float) });
            // Sellable 普通物品乘数为 2.2，以边界两侧的报价验证分档。
            float below = 999f / 2.2f, above = 1000f / 2.2f;
            Check("Standard quote boundary fixtures", (int)rawPrice.Invoke(network, new object[] { def, below }) == 999
                && (int)rawPrice.Invoke(network, new object[] { def, above }) == 1000);
            Check("999 silver allows 2500; 1000 silver allows 10", (int)rawLimit.Invoke(network, new object[] { def, below }) == 2500
                && (int)rawLimit.Invoke(network, new object[] { def, above }) == 10);
            Set(network, "tradeTurnover", 400000L);
            Check("Customer discount cannot change quantity tier", (int)rawLimit.Invoke(network, new object[] { def, above }) == 10);
            Set(network, "tradeTurnover", 0L);
            ThingDef chip = DefDatabase<ThingDef>.GetNamedSilentFail("PowerfocusChip");
            bool actualChip = chip != null;
            if (chip == null)
            {
                chip = Fixture("HighValueStack");
                chip.statBases.First(s => s.stat == StatDefOf.MarketValue).value = 1000f;
                chip.graphicData = ThingDefOf.ComponentIndustrial.graphicData;
                ResetAnalysis(network);
            }
            limitedStack = chip;
            Thing sample = CorporateNetwork.MakeOrderProduct(chip, null, QualityCategory.Normal);
            Check((actualChip ? "Actual powerfocus chip" : "Core-only synthetic high-value stack") + " cap is 10", network.MaxOrderQuantity(sample) == 10);
            sample.Destroy();
            Thing steel = CorporateNetwork.MakeOrderProduct(ThingDefOf.Steel, null, QualityCategory.Normal);
            Check("Ordinary steel retains bulk cap 2500", network.MaxOrderQuantity(steel) == 2500); steel.Destroy();
            int balance = context.SilverCount, orders = network.Orders.Count;
            foreach (CorporateOrderSpeed speed in Enum.GetValues(typeof(CorporateOrderSpeed)))
                Check(speed + " backend rejects 11 chips without payment", !network.PlaceOrder(context, chip, null, QualityCategory.Normal, 11, out _ , speed)
                    && context.SilverCount == balance && network.Orders.Count == orders);
            bool placed = network.PlaceOrder(context, chip, null, QualityCategory.Normal, 10, out string reason);
            Check("Backend accepts 10 chips: " + reason, placed);
            if (placed) network.CancelOrder(context, network.Orders.Last(), out _);
            Check("Backend rejects 11 nonstacking knives", !network.PlaceOrder(context, DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Knife"),
                ThingDefOf.Steel, QualityCategory.Normal, 11, out _));
        }

        private static int Fee(RecipeDef recipe, Thing product, int batches, float factor) =>
            (int)AccessTools.Method(typeof(CorporateNetwork), "ProcessingReward").Invoke(null, new object[] { recipe, product, batches, factor });

        private static void RunFees()
        {
            ThingDef boots = DefDatabase<ThingDef>.GetNamed("Mugirl_PoliceBoots");
            RecipeDef recipe = DefDatabase<RecipeDef>.AllDefs.First(r => r.products?.Count == 1 && r.products[0].thingDef == boots);
            foreach (ThingDef stuff in GenStuff.AllowedStuffsFor(boots).OrderBy(d => d.BaseMarketValue).Take(3))
            {
                Thing sample = ThingMaker.MakeThing(boots, stuff);
                double work = recipe.WorkAmountTotal(sample);
                int expected = (int)Math.Max(150, Math.Max(Math.Round((double)sample.MarketValue * 7 * 0.25f), Math.Ceiling(work * 7 * 20 / 2500)));
                Check("Actual boots fee includes " + stuff.defName + " work (fee=" + expected + ", work=" + work + ")", Fee(recipe, sample, 7, 0.25f) == expected);
                sample.Destroy();
            }
            Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
            var batch = new RecipeDef { workAmount = 2500, products = new List<ThingDefCountClass> { new ThingDefCountClass(ThingDefOf.Steel, 4) } };
            Check("Work floor counts batches, not four outputs per batch", Fee(batch, steel, 10, 0.25f) == 200);
            Check("Small order keeps minimum 150", Fee(batch, steel, 1, 0.25f) == 150);
            // NaN 会被原版解释为“改读成品 WorkToMake”，这里验证真正传到报价层的无穷工作量。
            batch.workAmount = float.PositiveInfinity;
            Check("Invalid work does not generate a paid offer", Fee(batch, steel, 10, 0.25f) == 0);
            steel.Destroy();
        }

        private static CorporateMission AddMission(CorporateNetwork network, int id, ThingDef product, int count)
        {
            var mission = new CorporateMission { id = id, kind = CorporateMissionKind.Processing, state = CorporateMissionState.Active,
                product = product, count = count, batches = count, cost = 482, reward = 159, deadline = CorporateNetwork.Now + 420000 };
            ((List<CorporateMission>)network.Missions).Add(mission); return mission;
        }

        private static Thing SpawnGoods(ThingDef def, ThingDef stuff = null, QualityCategory quality = QualityCategory.Normal, int count = 1)
        {
            Thing thing = CorporateNetwork.MakeOrderProduct(def, stuff, quality); thing.stackCount = count;
            IntVec3 cell = beacon.TradeableCells.First(c => c.InBounds(beacon.Map) && c.Standable(beacon.Map)
                && c.GetThingList(beacon.Map).All(t => t.def.category != ThingCategory.Item));
            GenSpawn.Spawn(thing, cell, beacon.Map); return thing;
        }

        private static void RunSubmission(CorporateNetwork network, CorporateTradeContext context)
        {
            ThingDef boots = DefDatabase<ThingDef>.GetNamed("Mugirl_PoliceBoots"), stuff = GenStuff.DefaultStuffFor(boots);
            CorporateMission mission = AddMission(network, 9301, boots, 2); mission.stuff = stuff;
            Thing excellent = SpawnGoods(boots, stuff, QualityCategory.Excellent);
            Thing good = SpawnGoods(boots, stuff, QualityCategory.Good);
            Thing normal = SpawnGoods(boots, stuff);
            Thing poor = SpawnGoods(boots, stuff, QualityCategory.Poor);
            Thing damaged = SpawnGoods(boots, stuff); damaged.HitPoints = damaged.MaxHitPoints / 2;
            context.Invalidate();
            mission.count = 1;
            Check("Enough normal items do not trigger quality warning", !network.MissionSubmissionNeedsQualityConfirmation(mission, context));
            mission.count = 2;
            Check("Shortage of normal items triggers quality warning", network.MissionSubmissionNeedsQualityConfirmation(mission, context));
            Check("Default submission consumes normal first and preserves higher grades", network.SubmitMissionProducts(mission, context)
                && mission.progress == 1 && normal.Destroyed && !good.Destroyed && !excellent.Destroyed);
            Check("Repeated unconfirmed submission preserves high quality", !network.SubmitMissionProducts(mission, context)
                && mission.progress == 1 && !good.Destroyed && !excellent.Destroyed);
            Check("Confirmed submission takes good before excellent", network.SubmitMissionProducts(mission, context, true)
                && mission.progress == 2 && good.Destroyed && !excellent.Destroyed && mission.state == CorporateMissionState.Ready);
            Check("Poor and damaged items remain untouched", !poor.Destroyed && !damaged.Destroyed);
            long balance = CountWorld(ThingDefOf.Silver);
            Check("Claim pays deposit plus fixed fee", network.ClaimMission(mission, context) && CountWorld(ThingDefOf.Silver) - balance == 641);
            Check("Completed mission cannot pay twice", !network.ClaimMission(mission, context));
            CorporateMission expired = AddMission(network, 9302, boots, 1); expired.stuff = stuff; expired.deadline = CorporateNetwork.Now;
            Check("Expired mission does not consume high quality even with opt-in", !network.SubmitMissionProducts(expired, context, true) && !excellent.Destroyed);
        }

        private static void RunDeposit(CorporateNetwork network, CorporateTradeContext context)
        {
            ThingDef meal = ThingDefOf.MealSimple;
            RecipeDef recipe = DefDatabase<RecipeDef>.AllDefs.First(r => r.AvailableNow && r.products?.Count == 1
                && r.products[0].thingDef == meal && r.products[0].count == 1);
            ThingDef benchDef = DefDatabase<ThingDef>.AllDefs.First(d => typeof(Building_WorkTable).IsAssignableFrom(d.thingClass) && d.AllRecipes.Contains(recipe));
            Thing bench = ThingMaker.MakeThing(benchDef, GenStuff.DefaultStuffFor(benchDef)); bench.SetFaction(Faction.OfPlayer);
            IntVec3 cell = GenRadial.RadialCellsAround(beacon.Position, 30, false).First(c => c.InBounds(beacon.Map)
                && GenAdj.OccupiedRect(c, Rot4.North, benchDef.size).All(v => v.InBounds(beacon.Map) && v.Standable(beacon.Map) && v.GetEdifice(beacon.Map) == null));
            GenSpawn.Spawn(bench, cell, beacon.Map);
            CorporateMission mission = AddMission(network, 9303, meal, 2);
            mission.state = CorporateMissionState.Available; mission.recipe = recipe; mission.acceptByTick = CorporateNetwork.Now + 60000;
            ThingDef rice = DefDatabase<ThingDef>.GetNamed("RawRice"); mission.ingredients.Add(new ThingDefCountClass(rice, 20));
            int silver = context.SilverCount; long ingredients = CountWorld(rice);
            Check("Accept deducts deposit once and supplies ingredients", network.AcceptMission(mission, context, out string reason)
                && context.SilverCount == silver - mission.cost && CountWorld(rice) - ingredients == 20);
            SpawnGoods(meal, count: 2); context.Invalidate();
            Check("Normal processing food can be submitted", network.SubmitMissionProducts(mission, context) && mission.state == CorporateMissionState.Ready);
            long before = CountWorld(ThingDefOf.Silver);
            Check("Deposit return makes net profit equal advertised fee", network.ClaimMission(mission, context)
                && CountWorld(ThingDefOf.Silver) - before - mission.cost == mission.reward);
        }

        private static long CountWorld(ThingDef def)
        {
            var things = new HashSet<Thing>(Find.CurrentMap.listerThings.AllThings);
            var queue = new Queue<IThingHolder>(things.OfType<IThingHolder>());
            var seen = new HashSet<IThingHolder>(); var children = new List<IThingHolder>();
            while (queue.Count > 0)
            {
                IThingHolder holder = queue.Dequeue(); if (!seen.Add(holder)) continue;
                ThingOwner contents = holder.GetDirectlyHeldThings();
                if (contents != null) foreach (Thing thing in contents) { things.Add(thing); if (thing is IThingHolder nested) queue.Enqueue(nested); }
                children.Clear(); holder.GetChildHolders(children);
                foreach (IThingHolder child in children) if (child != null) queue.Enqueue(child);
            }
            return things.Where(t => !t.Destroyed && t.def == def).Sum(t => (long)t.stackCount);
        }

        private static void RunLocalization()
        {
            string expected = File.ReadAllText(Path.Combine(output, "expected-language.txt"));
            bool chinese = expected == "ChineseSimplified";
            Check("Requested language is actually loaded: " + LanguageDatabase.activeLanguage.folderName,
                LanguageDatabase.activeLanguage.folderName.StartsWith(expected, StringComparison.Ordinal));
            foreach (string name in new[] { "Job_MugirlMilkingTargetWait", "Job_MugirlMilkInteractionWait", "Job_MugirlLancePointCharge", "Job_MugirlLanceLineCharge" })
            {
                string report = DefDatabase<JobDef>.GetNamed(name).reportString;
                Check(name + " localized report: " + report, !chinese || report.Any(c => c >= '\u4e00' && c <= '\u9fff'));
            }
            var mission = new CorporateMission { kind = CorporateMissionKind.Processing, product = ThingDefOf.MealSimple,
                count = 36, batches = 9, cost = 180, reward = 240 };
            Check("Description contains refundable deposit, fee and total without raw placeholders", mission.Description.Contains("180")
                && mission.Description.Contains("240") && mission.Description.Contains("420") && !mission.Description.Contains("{8}"));
            File.WriteAllText(Path.Combine(output, "mission-description.txt"), mission.Description);
        }

        private static void SaveLegacyContracts(CorporateNetwork network)
        {
            ThingDef chip = limitedStack;
            var goods = new List<Thing>();
            for (int i = 0; i < 250; i++) { Thing t = ThingMaker.MakeThing(chip); t.stackCount = 10; goods.Add(t); }
            CorporateOrder order = (CorporateOrder)AccessTools.Method(typeof(CorporateNetwork), "SaveOrder")
                .Invoke(network, new object[] { chip, 2500, 7500000, CorporateNetwork.Now + 60000, goods });
            oldOrderId = order.id;
            CorporateMission mission = AddMission(network, 9304, DefDatabase<ThingDef>.GetNamed("Mugirl_PoliceBoots"), 7);
            mission.stuff = GenStuff.DefaultStuffFor(mission.product);
            mission.recipe = DefDatabase<RecipeDef>.AllDefs.First(r => r.products?.Count == 1 && r.products[0].thingDef == mission.product);
            mission.ingredients.Add(new ThingDefCountClass(mission.stuff, mission.product.costStuffCount * mission.count));
            oldMissionId = mission.id;
            savedGame = Current.Game;
            GameDataSaveLoader.SaveGame("CorporateBugReportRoundtrip");
            File.WriteAllText(Path.Combine(output, "bug-progress.txt"), "Full save/load queued");
            GameDataSaveLoader.LoadGame("CorporateBugReportRoundtrip");
        }

        private static void Reloaded()
        {
            CorporateNetwork network = CorporateNetwork.Current;
            CorporateOrder order = network.Orders.Single(o => o.id == oldOrderId);
            Check("Full save/load preserves legacy 2500-item payment and quantity", order.quantity == 2500 && order.paid == 7500000
                && order.goods.Sum(t => t.stackCount) == 2500);
            CorporateMission mission = network.FindMission(oldMissionId);
            Check("Existing processing reward is not repriced on load", mission.reward == 159 && mission.cost == 482);
            order.readyTick = CorporateNetwork.Now;
            Check("Legacy paid order above new cap still delivers all goods", network.ClaimOrder(Context(), order, out _)
                && order.deliveredQuantity == 2500 && order.state == CorporateOrderState.Completed);
            // 隐藏启动时 Unity 可能不产生初始 GUI 帧；实际调整尺寸后再检查绘制。
            Screen.SetResolution(1400, 980, false);
            ShowOrder(); phase = 2; nextAt = Time.realtimeSinceStartup + 5f;
        }

        private static void ShowOrder()
        {
            Find.WindowStack.WindowOfType<LudeonTK.EditWindow_Log>()?.Close();
            window = new Window_CorporateComms(Context()); Find.WindowStack.Add(window);
            Set(window, "selectedPage", 2); Set(window, "requestedPage", 2);
            // 本探针检查详情控件，不以某个固定帧率假定分帧目录已构建完毕。
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while ((bool)AccessTools.Property(typeof(Window_CorporateComms), "OrderCatalogBusy").GetValue(window)
                && timer.ElapsedMilliseconds < 5000)
                AccessTools.Method(typeof(Window_CorporateComms), "UpdateOrderCatalogWork").Invoke(window, null);
            Set(window, "orderDef", limitedStack);
            Set(window, "orderQuantity", 2500); Set(window, "orderQuantityText", "2500");
            Set(window, "orderPageScroll", new Vector2(0f, 10000f));
            AccessTools.Method(typeof(Window_CorporateComms), "RefreshOrderPreview").Invoke(window, null);
        }

        private static void InspectLayout(Window_CorporateComms __instance, Rect rect)
        {
            if (__instance != window || Get<Thing>(window, "orderPreview")?.def != limitedStack) return;
            detailDraws++;
            bool compact = UI.screenWidth < 1000;
            if (compact ? checkedCompactLayout : checkedWideLayout) return;
            if (compact) checkedCompactLayout = true; else checkedWideLayout = true;
            GameFont oldFont = Text.Font; Text.Font = GameFont.Tiny;
            float height = Text.CalcHeight(CorporateNetwork.Current.OrderQuantityLimitReason(Get<Thing>(window, "orderPreview")), rect.width);
            Text.Font = oldFont;
            Check((compact ? "Compact" : "Wide") + " limit text fits its allocated space (height=" + height + ")", height <= 40f);
            Check((compact ? "Compact" : "Wide") + " limit text does not overlap quote breakdown", rect.height - 236f >= 334f);
        }

        private static void Finish()
        {
            Check("Wide and compact detail layouts were actually drawn", checkedWideLayout && checkedCompactLayout);
            phase = 8; File.WriteAllText(Path.Combine(output, "bug-complete.txt"), "PASS=" + passed + " FAIL=" + failed);
            Application.Quit();
        }
    }
}
