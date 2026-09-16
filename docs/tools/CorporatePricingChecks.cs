// Isolated validation only. RunContract spends silver and cancels its own test order.
using System;
using System.Collections.Generic;
using System.Linq;
using Mugirl;
using RimWorld;
using UnityEngine;
using Verse;

public static class CorporatePricingChecks
{
    public static void RunQuotes(CorporateNetwork network, Action<string, bool> check)
    {
        CorporateTradeSettingsDef settings = network.TradeSettings;
        check("Live pricing settings load the rare, archotech and special procurement tiers",
            settings.rareOrderPriceFactor == 6f && settings.archotechOrderPriceFactor == 10f
            && settings.specialOrderPriceFactor == 20f && settings.specialOrderMinimumUnitPrice == 20000);
        CheckQuote(network, "Steel", 2.2f, 0, 3, 5, false, check);
        CheckQuote(network, "ComponentSpacer", 6f, 0, 12, 20, false, check);
        CheckQuote(network, "Hyperweave", 6f, 0, 12, 20, false, check);
        CheckQuote(network, "MedicineUltratech", 6f, 0, 12, 20, false, check);
        CheckQuote(network, "ArchotechArm", 10f, 0, 20, 35, false, check);
        CheckQuote(network, "Neurotrainer_Crafting", 6f, 0, 12, 20, false, check);
        foreach (string defName in new[] { "MechSerumHealer", "MechSerumResurrector", "TechprofSubpersonaCore", "AIPersonaCore" })
            CheckQuote(network, defName, 20f, 20000, 30, 60, true, check);
        if (ModsConfig.RoyaltyActive) CheckQuote(network, "PsychicAmplifier", 20f, 20000, 30, 60, true, check);
        if (ModsConfig.AnomalyActive)
            foreach (string defName in new[] { "Shard", "RevenantSpine", "Shell_Deadlife" })
                CheckQuote(network, defName, 20f, 20000, 30, 60, true, check);
        check("Weekly offers contain no special-procurement goods",
            network.Stock.All(s => s.sample != null && !network.IsSpecialOrder(s.sample.def)));
        check("New rare weekly stock retains the full procurement price floor",
            network.Stock.Where(s => network.IsRareOrder(s.sample.def))
                .All(s => s.unitPrice >= network.OrderQuote(s.sample, 1)));
        check("Items forbidden from trade remain excluded from the order catalogue",
            !network.OrderCatalog.Any(d => d.tradeability == Tradeability.None));
        ThingDef mechlink = DefDatabase<ThingDef>.GetNamedSilentFail("Mechlink");
        if (mechlink != null) check("Special-generation mechlinks remain excluded", !network.IsOrderable(mechlink));
        CheckSpecialFloor(network, check);
        CheckTierPrecedence(network, check);
    }

    private static void CheckSpecialFloor(CorporateNetwork network, Action<string, bool> check)
    {
        Thing preview = CorporateNetwork.MakeProduct(DefDatabase<ThingDef>.GetNamed("MechSerumHealer"), null, QualityCategory.Normal);
        try
        {
            preview.HitPoints = 1;
            float scaled = preview.MarketValue * 20f;
            int expected = CorporateNetwork.Price(Math.Max(scaled, 20000f), 3);
            check("Special minimum is charged per unit, including reduced-value goods",
                expected >= 60000 && network.OrderQuote(preview, 3) == expected);
        }
        finally { preview.Destroy(); }
    }

    private static void CheckQuote(CorporateNetwork network, string defName, float factor, int minimum,
        int firstDay, int lastDay, bool special, Action<string, bool> check)
    {
        ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        check(defName + " exists in the real loaded order catalogue", def != null && network.IsOrderable(def));
        if (def == null || !network.IsOrderable(def)) return;
        Thing preview = CorporateNetwork.MakeProduct(def, null, QualityCategory.Normal);
        try
        {
            int expected = CorporateNetwork.Price(Math.Max(preview.MarketValue * factor, minimum), 3);
            IntRange days = network.OrderLeadTime(def);
            check(defName + " charges its actual market value at the expected tier (three units: " + expected + ")",
                network.OrderQuote(preview, 3) == expected && network.IsSpecialOrder(def) == special);
            check(defName + " quotes " + firstDay + "-" + lastDay + " preparation days",
                days.min == firstDay && days.max == lastDay);
        }
        finally { preview.Destroy(); }
    }

    private static void CheckTierPrecedence(CorporateNetwork network, Action<string, bool> check)
    {
        CorporateTradeSettingsDef settings = network.TradeSettings;
        float originalFactor = settings.rareOrderPriceFactor;
        IntRange originalDays = settings.rareOrderDays;
        Thing preview = CorporateNetwork.MakeProduct(DefDatabase<ThingDef>.GetNamed("MechSerumHealer"), null, QualityCategory.Normal);
        try
        {
            settings.rareOrderPriceFactor = 24f;
            settings.rareOrderDays = new IntRange(70, 90);
            IntRange days = network.OrderLeadTime(preview.def);
            check("Overlapping tiers always retain the highest configured price and preparation range",
                network.OrderQuote(preview, 1) == CorporateNetwork.Price(Math.Max(preview.MarketValue * 24f, 20000f), 1)
                && days.min == 70 && days.max == 90);
        }
        finally
        {
            settings.rareOrderPriceFactor = originalFactor;
            settings.rareOrderDays = originalDays;
            preview.Destroy();
        }
    }

    public static void RunContract(CorporateNetwork network, CorporateTradeContext context, Action<string, bool> check)
    {
        CheckLegacyRareOffer(network, context, check);
        CheckLegacySpecialOffer(network, context, check);
        ThingDef def = DefDatabase<ThingDef>.GetNamed("MechSerumHealer");
        Thing preview = CorporateNetwork.MakeProduct(def, null, QualityCategory.Normal);
        int quote;
        try { quote = network.OrderQuote(preview, 1); }
        finally { preview.Destroy(); }
        if (context.SilverCount < quote) throw new InvalidOperationException("Pricing contract fixture requires " + quote + " silver.");
        int oldSilver = context.SilverCount;
        int signedTick = CorporateNetwork.Now;
        bool accepted = network.PlaceOrder(context, def, null, QualityCategory.Normal, 1, out string reason);
        check("Special order signs at the displayed quote: " + (reason ?? "ok"), accepted);
        if (!accepted) throw new InvalidOperationException(reason);
        CorporateOrder order = network.Orders.Last();
        check("Special order debits and records exactly the displayed price",
            order.paid == quote && oldSilver - context.SilverCount == quote);
        check("Special order locks a real 30-60 day preparation period",
            order.readyTick >= signedTick + 30 * CorporateNetwork.DayTicks
            && order.readyTick <= signedTick + 60 * CorporateNetwork.DayTicks
            && order.goods.Count == 1 && order.goods[0].holdingOwner == network.Vault);
        int signedDue = order.readyTick;
        CorporateTradeSettingsDef settings = network.TradeSettings;
        float originalFactor = settings.specialOrderPriceFactor;
        IntRange originalDays = settings.specialOrderDays;
        try
        {
            settings.specialOrderPriceFactor = 40f;
            settings.specialOrderDays = new IntRange(80, 100);
            network.OrderQuote(order.goods[0], 1);
            network.OrderLeadTime(def);
            check("Subsequent price changes preserve the signed amount and arrival date",
                order.paid == quote && order.readyTick == signedDue);
        }
        finally
        {
            settings.specialOrderPriceFactor = originalFactor;
            settings.specialOrderDays = originalDays;
        }
        // Map refunds are still inside incoming drop pods while the game is paused.
        long beforeRefund = CountSilverIncludingDelivery(context);
        bool cancelled = network.CancelOrder(context, order, out reason);
        check("Special order cancellation refunds the original contract amount and clears its escrow",
            cancelled && CountSilverIncludingDelivery(context) - beforeRefund == Mathf.FloorToInt(quote * settings.cancelRefundFactor)
            && order.state == CorporateOrderState.Cancelled && order.goods.Count == 0);
    }

    private static void CheckLegacyRareOffer(CorporateNetwork network, CorporateTradeContext context, Action<string, bool> check)
    {
        network.EnsureWeeklyOffers();
        CorporateStock offer = network.Stock.First();
        Thing originalSample = offer.sample;
        int originalPrice = offer.unitPrice;
        int originalCount = offer.count;
        Thing rare = CorporateNetwork.MakeProduct(ThingDefOf.ComponentSpacer, null, QualityCategory.Normal);
        Thing ordinary = CorporateNetwork.MakeProduct(ThingDefOf.Steel, null, QualityCategory.Normal);
        try
        {
            offer.sample = ordinary;
            offer.unitPrice = 7;
            check("Ordinary legacy stock retains its saved unit price", network.StockUnitPrice(offer) == 7);
            offer.sample = rare;
            offer.unitPrice = 1;
            offer.count = 2;
            int displayedPrice = network.StockUnitPrice(offer);
            int procurementPrice = network.OrderQuote(rare, 1);
            check("Rare legacy stock displays the current procurement price floor", displayedPrice == procurementPrice);
            offer.unitPrice = procurementPrice + 100;
            check("Higher saved rare stock prices are never reduced", network.StockUnitPrice(offer) == procurementPrice + 100);
            offer.unitPrice = 1;
            int silver = context.SilverCount;
            int orderCount = network.Orders.Count;
            bool bought = network.BuyStock(context, offer, 1, out string reason);
            CorporateOrder receipt = network.Orders.LastOrDefault();
            check("Rare legacy stock charges exactly its displayed price: " + (reason ?? "ok"),
                bought && silver - context.SilverCount == displayedPrice && offer.count == 1
                && network.Orders.Count == orderCount + 1 && receipt != null && receipt.paid == displayedPrice
                && receipt.def == rare.def && receipt.quantity == 1);
        }
        finally
        {
            offer.sample = originalSample;
            offer.unitPrice = originalPrice;
            offer.count = originalCount;
            rare.Destroy();
            ordinary.Destroy();
        }
    }

    private static void CheckLegacySpecialOffer(CorporateNetwork network, CorporateTradeContext context, Action<string, bool> check)
    {
        network.EnsureWeeklyOffers();
        CorporateStock offer = network.Stock.First();
        Thing originalSample = offer.sample;
        int originalPrice = offer.unitPrice;
        int originalCount = offer.count;
        Thing special = CorporateNetwork.MakeProduct(DefDatabase<ThingDef>.GetNamed("MechSerumHealer"), null, QualityCategory.Normal);
        try
        {
            // Represent a special item already serialized into a legacy cheap weekly offer.
            offer.sample = special;
            offer.unitPrice = 1;
            offer.count = 1;
            int silver = context.SilverCount;
            int orderCount = network.Orders.Count;
            bool bought = network.BuyStock(context, offer, 1, out _);
            check("Legacy weekly offers cannot bypass special procurement prices or lead times",
                !bought && context.SilverCount == silver && network.Orders.Count == orderCount && offer.count == 1);
        }
        finally
        {
            offer.sample = originalSample;
            offer.unitPrice = originalPrice;
            offer.count = originalCount;
            special.Destroy();
        }
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
            if (contents != null)
                foreach (Thing thing in contents)
                {
                    things.Add(thing);
                    if (thing is IThingHolder nested) holders.Enqueue(nested);
                }
            children.Clear();
            holder.GetChildHolders(children);
            foreach (IThingHolder child in children) if (child != null) holders.Enqueue(child);
        }
        return things.Where(t => !t.Destroyed && t.def == ThingDefOf.Silver).Sum(t => (long)t.stackCount);
    }
}
