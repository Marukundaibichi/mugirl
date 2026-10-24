using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Mugirl.GameValidation.Core;

namespace Mugirl.GameValidation.Suites;

/// <summary>在真实 Def 目录中模拟通用组件注入，比较旧构建并验证订单、现货及已付款交付。</summary>
internal static class CorporateSupplySuite
{
    public const string Name = "corporate-supply";
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static bool Check(ValidationContext ctx, bool condition, string what, Func<string>? detail = null)
        => ctx.Report.Check(condition, what, detail ?? (() => "unexpected result"));

    public static ValidationSuite Create() => new(Name,
        "global and partial component injection, supply protection and real order/stock payment",
        SuiteNeeds.MugirlMod, [new ValidationStep("supply", Run, TimeoutSeconds: 240)], inDefaultRun: false)
    {
        InAllRun = false,
        LogPolicy = SuiteLogPolicy.AnyError,
    };

    private static IEnumerable<Wait> Run(ValidationContext ctx)
    {
        Assembly assembly = ctx.RequireTarget().Assembly!;
        Type networkType = LegacyApi.FindType(assembly, "CorporateNetwork")!;
        object network = LegacyApi.GameComponent(networkType) ?? throw new InvalidOperationException("No corporate network.");
        Type analysisType = LegacyApi.FindType(assembly, "CorporateSupplyAnalysis")!;
        object cache = analysisType.GetField("SharedCache", Members)!.GetValue(null)!;
        FieldInfo snapshot = cache.GetType().GetField("snapshot", Members)!;
        object? originalSnapshot = snapshot.GetValue(cache);
        void Refresh() => snapshot.SetValue(cache, null);
        MethodInfo canSupply = networkType.GetMethod("CanAutomaticallySupply")!;
        bool Can(ThingDef def) => (bool)canSupply.Invoke(network, [def])!;
        ThingDef[] catalog = ((IEnumerable<ThingDef>)networkType.GetProperty("OrderCatalog")!.GetValue(network)!).ToArray();
        var originals = catalog.ToDictionary(d => d, d => d.comps);
        Dictionary<ThingDef, bool> initial = catalog.ToDictionary(d => d, Can);
        bool baseline = ctx.Options.SuiteOption(Name + ".mode", "fixed") == "baseline";
        var observations = new List<object>();
        int initiallyAvailable = initial.Count(pair => pair.Value);
        Check(ctx, initiallyAvailable > 100, "unmodified environment has a usable catalog", () => initiallyAvailable.ToString());

        void Restore()
        {
            foreach (var pair in originals) { pair.Key.comps = pair.Value; }
            Refresh();
        }

        void Inject(IEnumerable<ThingDef> defs, Type comp)
        {
            foreach (ThingDef def in defs)
            {
                def.comps = [.. def.comps ?? [], new CompProperties { compClass = comp }];
            }
            Refresh();
        }

        void Measure(string scenario, bool all)
        {
            string[] lost = catalog.Where(d => initial[d] && !Can(d)).Select(d => d.defName).ToArray();
            string[] gained = catalog.Where(d => !initial[d] && Can(d)).Select(d => d.defName).ToArray();
            observations.Add(new { scenario, initiallyAvailable, lost, gained });
            ctx.Report.Info($"{scenario}: initially {initiallyAvailable}, lost {lost.Length}, gained {gained.Length}");
            Check(ctx, baseline ? (all ? lost.Length == initiallyAvailable : lost.Length > 0 && lost.Length < initiallyAvailable)
                : lost.Length == 0 && gained.Length == 0,
                scenario + (baseline ? " reproduces the old rejection" : " preserves every supply decision"),
                () => $"lost {lost.Length}: {string.Join(", ", lost.Take(8))}; gained {gained.Length}");
        }

        try
        {
            Inject(catalog, typeof(SupplySaveComp));
            Measure("save component on all items", all: true);
            Restore();

            ThingDef medicine = ThingDefOf.MedicineIndustrial;
            Check(ctx, initial.TryGetValue(medicine, out bool available) && available, "medicine is available before injection");
            Inject([medicine], typeof(SupplyBookkeepingComp));
            Measure("bookkeeping component on medicine only", all: false);
            Restore();

            if (!baseline)
            {
                Inject(catalog, typeof(SupplyBookkeepingComp));
                Measure("bookkeeping component on all items", all: true);
                Transactions(ctx, assembly, network, medicine, Can, Refresh);
            }
        }
        finally
        {
            Restore();
            snapshot.SetValue(cache, originalSnapshot);
            if (ctx.ArtifactPath("corporate-supply-environments.json") is { } path)
            {
                File.WriteAllText(path, JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        yield return Wait.Frames(1);
    }

    private static void Transactions(ValidationContext ctx, Assembly assembly, object network, ThingDef medicine,
        Func<ThingDef, bool> canSupply, Action refresh)
    {
        Type networkType = network.GetType();
        Type introType = LegacyApi.FindType(assembly, "CorporateIntroduction")!;
        object intro = LegacyApi.GameComponent(introType)!;
        object contact = LegacyApi.Get(intro, "contact")!;
        object? wasOpen = LegacyApi.Get(contact, "accountOpened");
        LegacyApi.Set(contact, "accountOpened", true);
        Type contextType = LegacyApi.FindType(assembly, "CorporateTradeContext")!;
        object context = Activator.CreateInstance(contextType, [ctx.Map, null])!;
        object speed = Enum.Parse(LegacyApi.FindType(assembly, "CorporateOrderSpeed")!, "Standard");
        var beacon = (Building_OrbitalTradeBeacon)ThingMaker.MakeThing(ThingDefOf.OrbitalTradeBeacon);
        beacon.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(beacon, GameFixtures.FreeCellNear(ctx.Map, ctx.Map.Center, 12), ctx.Map);
        beacon.GetComp<CompPowerTrader>().PowerOn = true;
        Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
        silver.stackCount = 500;
        GenSpawn.Spawn(silver, GameFixtures.FreeCellNear(ctx.Map, beacon.Position + IntVec3.East, 2), ctx.Map);
        int Balance() => (int)contextType.GetProperty("SilverCount")!.GetValue(context)!;
        bool Place(int quantity, out string? reason)
        {
            object?[] args = [context, medicine, null, QualityCategory.Normal, quantity, null, speed];
            bool result = (bool)networkType.GetMethod("PlaceOrder")!.Invoke(network, args)!;
            reason = args[5]?.ToString();
            return result;
        }

        List<CompProperties> medicineComps = medicine.comps;
        List<ThingDefCountClass>? originalSmelt = ThingDefOf.WoodLog.smeltProducts;
        try
        {
            object?[] permission = [context, null];
            if (!Check(ctx, (bool)networkType.GetMethod("CanTrade")!.Invoke(network, permission)!,
                "test account can trade", () => permission[1]?.ToString() ?? "unknown reason")) { return; }
            Check(ctx, Balance() >= 500, "real silver is within a powered trade beacon");
            Thing preview = (Thing)networkType.GetMethod("MakeOrderProduct")!.Invoke(null, [medicine, null, QualityCategory.Normal])!;
            int quote = (int)LegacyApi.Method(networkType, "OrderQuote", 3)!.Invoke(network, [preview, 2, speed])!;
            networkType.GetMethod("DiscardUnspawnedProduct", Members)!.Invoke(null, [preview]);
            int before = Balance();
            bool placed = Place(2, out string? problem);
            if (!Check(ctx, placed, "order with generic component succeeds", () => problem ?? "no reason")) { return; }
            Check(ctx, before - Balance() == quote && quote > 0, "order deducts the quoted amount exactly once");
            IList orders = (IList)networkType.GetProperty("Orders")!.GetValue(network)!;
            object paidOrder = orders[orders.Count - 1]!;
            Check(ctx, (int)LegacyApi.Get(paidOrder, "quantity")! == 2
                && ((IEnumerable)LegacyApi.Get(paidOrder, "goods")!).Cast<Thing>().Sum(t => t.stackCount) == 2,
                "paid order holds the requested physical goods");

            before = Balance();
            Check(ctx, !Place(2501, out _) && Balance() == before, "invalid order quantity does not charge silver");

            LegacyApi.Method(networkType, "EnsureWeeklyOffers", 0)!.Invoke(network, null);
            object offer = Activator.CreateInstance(LegacyApi.FindType(assembly, "CorporateStockOffer")!)!;
            Thing sample = (Thing)networkType.GetMethod("MakeOrderProduct")!.Invoke(null, [medicine, null, QualityCategory.Normal])!;
            ((IThingHolder)network).GetDirectlyHeldThings().TryAdd(sample, false);
            LegacyApi.Set(offer, "sample", sample);
            LegacyApi.Set(offer, "count", 4);
            IList stock = (IList)networkType.GetProperty("Stock")!.GetValue(network)!;
            stock.Add(offer);
            int stockQuote = (int)networkType.GetMethod("StockQuote")!.Invoke(network, [offer, 2])!;
            before = Balance();
            object?[] buyArgs = [context, offer, 2, null];
            bool bought = (bool)networkType.GetMethod("BuyStock")!.Invoke(network, buyArgs)!;
            Check(ctx, bought && before - Balance() == stockQuote && (int)LegacyApi.Get(offer, "count")! == 2,
                "stock with generic component purchases and charges correctly", () => buyArgs[3]?.ToString() ?? "amount/count mismatch");

            // 在已付款后给同一物品加入真实生成器，确认新采购拒绝，但已有合同继续交付。
            medicine.comps = [.. medicineComps, new CompProperties_Spawner { thingToSpawn = ThingDefOf.Gold, spawnCount = 1 }];
            ThingDefOf.WoodLog.smeltProducts = [new ThingDefCountClass(medicine, 1)];
            refresh();
            Check(ctx, !canSupply(medicine), "known producer remains restricted with generic components present");
            Check(ctx, !canSupply(ThingDefOf.WoodLog), "genuine production restriction still propagates upstream");
            before = Balance();
            Check(ctx, !Place(1, out _) && Balance() == before, "restricted new order does not charge silver");
            buyArgs = [context, offer, 1, null];
            Check(ctx, !(bool)networkType.GetMethod("BuyStock")!.Invoke(network, buyArgs)! && Balance() == before,
                "old stock cannot bypass a real supply restriction");
            LegacyApi.Set(paidOrder, "readyTick", Find.TickManager.TicksGame);
            object?[] claimArgs = [context, paidOrder, null];
            bool claimed = (bool)networkType.GetMethod("ClaimOrder")!.Invoke(network, claimArgs)!;
            Check(ctx, claimed && (int)LegacyApi.Get(paidOrder, "deliveredQuantity")! == 2 && Balance() == before,
                "paid contract delivers despite a later supply restriction without charging again",
                () => claimArgs[2]?.ToString() ?? "delivery/amount mismatch");
            claimArgs = [context, paidOrder, null];
            Check(ctx, !(bool)networkType.GetMethod("ClaimOrder")!.Invoke(network, claimArgs)!, "paid contract cannot be claimed twice");
        }
        finally
        {
            medicine.comps = medicineComps;
            ThingDefOf.WoodLog.smeltProducts = originalSmelt;
            LegacyApi.Set(contact, "accountOpened", wasOpen!);
            if (!silver.Destroyed) { silver.Destroy(); }
            if (!beacon.Destroyed) { beacon.Destroy(); }
            refresh();
        }
    }
}

/// <summary>模拟截图中给大量物品注入的独立存档状态；不依赖 DelayedWealth 的程序集。</summary>
public class SupplySaveComp : ThingComp
{
    private int age;
    public override void PostExposeData() => Scribe_Values.Look(ref age, "supplyProbeAge", 0);
}

/// <summary>模拟附带初始化、计时、堆叠与属性通知的记账组件。</summary>
public sealed class SupplyBookkeepingComp : SupplySaveComp
{
    public override void PostPostMake() => base.PostPostMake();
    public override void PostSpawnSetup(bool respawningAfterLoad) => base.PostSpawnSetup(respawningAfterLoad);
    public override void CompTick() => base.CompTick();
    public override void PostSplitOff(Thing piece) => base.PostSplitOff(piece);
    public override float GetStatFactor(StatDef stat) => base.GetStatFactor(stat);
}
