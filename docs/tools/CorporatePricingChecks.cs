// 仅供显式启用的隔离验证；合成 Def 不会写入正式游戏数据。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mugirl;
using RimWorld;
using Verse;

public static class CorporatePricingChecks
{
    private static ThingDef cheap, boundary, expensive, payout, opaque, cycleA, cycleB;
    public sealed class UnknownEffect : CompUseEffect { public override void DoEffect(Pawn usedBy) { } }

    private static ThingDef Fixture(string name, float value)
    {
        var def = new ThingDef {
            defName = "CorporatePricingProbe_" + name, label = name,
            thingClass = typeof(ThingWithComps), category = ThingCategory.Item,
            tradeability = Tradeability.Sellable, stackLimit = 75, destroyable = true, techLevel = TechLevel.Industrial,
            statBases = new List<StatModifier> {
                new StatModifier { stat = StatDefOf.MarketValue, value = value },
                new StatModifier { stat = StatDefOf.MaxHitPoints, value = 100 }
            }, comps = new List<CompProperties>()
        };
        DefDatabase<ThingDef>.Add(def);
        return def;
    }

    private static void Fixtures(CorporateNetwork network)
    {
        if (cheap == null)
        {
            cheap = Fixture("Cheap", 0.5f);
            boundary = Fixture("Boundary", 499f);
            expensive = Fixture("Expensive", 500f);
            payout = Fixture("DeclaredSilverOutput", 1f);
            payout.butcherProducts = new List<ThingDefCountClass> { new ThingDefCountClass(ThingDefOf.Silver, 5000) };
            opaque = Fixture("OpaqueEffect", 1f);
            opaque.comps.Add(new CompProperties { compClass = typeof(UnknownEffect) });
            cycleA = Fixture("CycleA", 1f);
            cycleB = Fixture("CycleB", 1f);
            cycleA.butcherProducts = new List<ThingDefCountClass> { new ThingDefCountClass(cycleB, 2) };
            cycleB.butcherProducts = new List<ThingDefCountClass> { new ThingDefCountClass(cycleA, 2) };
        }
        typeof(CorporateNetwork).GetField("supplyAnalysis", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(network, null);
    }

    private static int Quote(CorporateNetwork network, ThingDef def, int count = 1, CorporateOrderSpeed speed = CorporateOrderSpeed.Standard)
    {
        Thing sample = CorporateNetwork.MakeOrderProduct(def, GenStuff.DefaultStuffFor(def), QualityCategory.Normal);
        try { return network.OrderQuote(sample, count, speed); }
        finally { sample.Destroy(); }
    }

    public static void RunQuotes(CorporateNetwork network, Action<string, bool> check)
    {
        Fixtures(network);
        check("Low-value Sellable batch remains 110", Quote(network, cheap, 100) == 110);
        MethodInfo rawQuote = typeof(CorporateNetwork).GetMethod("OrderQuote", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(ThingDef), typeof(float), typeof(int), typeof(CorporateOrderSpeed) }, null);
        int below = (int)rawQuote.Invoke(network, new object[] { boundary, 499f, 1, CorporateOrderSpeed.Standard });
        int above = (int)rawQuote.Invoke(network, new object[] { expensive, 500f, 1, CorporateOrderSpeed.Standard });
        check("No market-value 500 tier cliff (" + below + "/" + above + ")", below == 1098 && above == 1100);
        check("Opaque scripted effect forbids automatic duplication", !network.CanAutomaticallySupply(opaque));
        check("Declared 5000-silver output costs at least 6000", Quote(network, payout) == 6000);
        check("Amplifying conversion cycle is restricted", !network.CanAutomaticallySupply(cycleA) && !network.CanAutomaticallySupply(cycleB));
        check("Fractional batch rounds once", CorporateNetwork.Price(1.1f, 100) == 110);
        check("Express adds 50 percent with final rounding", CorporateNetwork.ExpeditedTotal(101, CorporateOrderSpeed.Express) == 152);
        check("Priority doubles price", CorporateNetwork.ExpeditedTotal(101, CorporateOrderSpeed.Priority) == 202);
        check("Three-day priority is eighteen hours", CorporateNetwork.PreparationTicks(3, CorporateOrderSpeed.Priority) == 45000);
        check("Five-day express is sixty hours", CorporateNetwork.PreparationTicks(5, CorporateOrderSpeed.Express) == 150000);
        check("Invalid speed and overflow are rejected", CorporateNetwork.ExpeditedTotal(100, (CorporateOrderSpeed)9) == 0
            && CorporateNetwork.ExpeditedTotal(60000000, CorporateOrderSpeed.Priority) == 0);
        ThingDef meat = DefDatabase<ThingDef>.GetNamedSilentFail("Meat_Twisted");
        if (meat != null)
        {
            check("Actual twisted meat remains available", network.CanAutomaticallySupply(meat));
            check("Actual 100 twisted meat costs 110", Quote(network, meat, 100) == 110);
            check("Twisted meat has normal lead time", network.OrderLeadTime(meat).min == 3 && network.OrderLeadTime(meat).max == 5);
            Thing sample = CorporateNetwork.MakeOrderProduct(meat, null, QualityCategory.Normal);
            try
            {
                var stock = new CorporateStock { sample = sample, count = 100, unitPrice = 20000 };
                check("Old inflated stock is repriced", network.StockUnitPrice(stock) < 500);
                check("Stock batch rounds once to 83", network.StockQuote(stock, 100) == 83);
                check("Batch affordability permits 100 units for 83 silver", network.AffordableStockQuantity(stock, 83) == 100);
            }
            finally { sample.Destroy(); }
        }
        ThingDef bow = DefDatabase<ThingDef>.GetNamedSilentFail("Bow_Great_Unique");
        if (bow != null)
        {
            Thing a = CorporateNetwork.MakeOrderProduct(bow, null, QualityCategory.Normal);
            Thing b = CorporateNetwork.MakeOrderProduct(bow, null, QualityCategory.Normal);
            try
            {
                check("Unique greatbow has no 20000 floor", network.OrderQuote(a, 1) < 20000);
                check("Unique greatbow preview matches delivery", a.MarketValue == b.MarketValue
                    && a.TryGetComp<CompUniqueWeapon>().TraitsListForReading.SequenceEqual(b.TryGetComp<CompUniqueWeapon>().TraitsListForReading));
            }
            finally { a.Destroy(); b.Destroy(); }
        }
        FieldInfo turnover = typeof(CorporateNetwork).GetField("tradeTurnover", BindingFlags.Instance | BindingFlags.NonPublic);
        long previous = (long)turnover.GetValue(network);
        try
        {
            turnover.SetValue(network, 400000L);
            check("Discount cannot undercut redemption floor", Quote(network, payout) == 6000);
            check("Discount precedes express surcharge", Quote(network, cheap, 100, CorporateOrderSpeed.Express) == 141);
        }
        finally { turnover.SetValue(network, previous); }
    }

    public static void RunContract(CorporateNetwork network, CorporateTradeContext context, Action<string, bool> check)
    {
        foreach (CorporateOrderSpeed speed in Enum.GetValues(typeof(CorporateOrderSpeed)))
        {
            int standard = Quote(network, ThingDefOf.Steel, 10);
            int expected = speed == CorporateOrderSpeed.Standard ? standard : speed == CorporateOrderSpeed.Express ? (int)Math.Ceiling(standard * 1.5m) : standard * 2;
            int before = context.SilverCount, now = CorporateNetwork.Now;
            bool ok = network.PlaceOrder(context, ThingDefOf.Steel, null, QualityCategory.Normal, 10, out string reason, speed);
            check(speed + " real order accepted: " + reason, ok);
            if (!ok) continue;
            CorporateOrder order = network.Orders.Last();
            check(speed + " debits displayed price and stores fee", before - context.SilverCount == expected
                && order.paid == expected && order.expediteFee == expected - standard && order.speed == speed);
            IntRange ticks = network.OrderLeadTicks(ThingDefOf.Steel, speed);
            check(speed + " arrival fits scaled range", order.readyTick >= now + ticks.min && order.readyTick <= now + ticks.max);
            long beforeRefund = CountSilverIncludingDelivery(context);
            bool cancelled = network.CancelOrder(context, order, out reason);
            check(speed + " refunds 80 percent including expedite fee", cancelled
                && CountSilverIncludingDelivery(context) - beforeRefund == (int)Math.Floor(order.paid * 0.8f));
        }
        int silver = context.SilverCount, count = network.Orders.Count;
        check("Opaque order rejected before payment", !network.PlaceOrder(context, opaque, null, QualityCategory.Normal, 1, out _)
            && context.SilverCount == silver && network.Orders.Count == count);
        Thing sample = CorporateNetwork.MakeProduct(opaque, null, QualityCategory.Normal);
        var offer = new CorporateStock { sample = sample, count = 5, unitPrice = 1 };
        ((List<CorporateStock>)network.Stock).Add(offer);
        try { check("Legacy stock cannot bypass restriction", !network.BuyStock(context, offer, 1, out _) && context.SilverCount == silver); }
        finally { ((List<CorporateStock>)network.Stock).Remove(offer); sample.Destroy(); }
    }

    private static long CountSilverIncludingDelivery(CorporateTradeContext context)
    {
        if (context.Map == null) return context.SilverCount;
        var things = new HashSet<Thing>(context.Map.listerThings.AllThings);
        var holders = new Queue<IThingHolder>(things.OfType<IThingHolder>());
        var visited = new HashSet<IThingHolder>();
        var children = new List<IThingHolder>();
        while (holders.Count > 0)
        {
            IThingHolder holder = holders.Dequeue();
            if (!visited.Add(holder)) continue;
            ThingOwner contents = holder.GetDirectlyHeldThings();
            if (contents != null) foreach (Thing thing in contents)
            { things.Add(thing); if (thing is IThingHolder nested) holders.Enqueue(nested); }
            children.Clear(); holder.GetChildHolders(children);
            foreach (IThingHolder child in children) if (child != null) holders.Enqueue(child);
        }
        return things.Where(t => !t.Destroyed && t.def == ThingDefOf.Silver).Sum(t => (long)t.stackCount);
    }
}
