// Conditional validation only. Uses real loaded Defs and the funded disposable colony.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mugirl;
using RimWorld;
using Verse;

public static class CorporateCatalogCleanupChecks
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(CorporateNetwork network, CorporateTradeContext context, Action<string, bool> check)
    {
        check("Catalogue exclusion regression uses an otherwise valid trade context", network.CanTrade(context, out _));
        ThingDef node = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_CerebrexNode");
        check("Cerebrex cleanup regression uses the real non-destroyable item", node != null && !node.destroyable);
        if (node == null) return;
        List<ThingDef> defs = network.OrderCatalog.ToList();
        check("Non-destroyable Cerebrex node is excluded from the order catalogue", !defs.Contains(node) && !network.IsOrderable(node));
        var originalFlags = DefDatabase<ThingDef>.AllDefsListForReading.ToDictionary(d => d, d => d.destroyable);
        FieldInfo destructionOverride = typeof(Thing).GetField("allowDestroyNonDestroyable", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        check("Global protected-item destruction override is observable", destructionOverride != null);
        object originalOverride = destructionOverride?.GetValue(null);
        Thing[] initialVault = network.Vault.ToArray();
        var window = new Window_CorporateComms(context);
        try
        {
            Func<ThingDef, int> quote = d => (int)typeof(Window_CorporateComms)
                .GetMethod("CatalogUnitPrice", InstanceFields).Invoke(window, new object[] { d });
            check("Direct catalogue quote cannot expose the protected Cerebrex node", quote(node) == 0 && !Cache(window).ContainsKey(node));
            var prices = defs.ToDictionary(d => d, quote);
            check("All " + defs.Count + " loaded catalogue goods can be quoted and cached repeatedly",
                prices.All(pair => quote(pair.Key) == pair.Value)
                && Cache(window).Count == defs.Count && MarketCache(window).Count == defs.Count);
            check("Catalogue quoting does not add goods to corporate custody", SameVault(network, initialVault));

            MethodInfo sort = typeof(Window_CorporateComms).GetMethod("SortCatalog", BindingFlags.Static | BindingFlags.NonPublic)
                .MakeGenericMethod(typeof(ThingDef));
            foreach (int direction in new[] { 1, 2 })
            {
                // A fresh cache exercises temporary product cleanup inside the sort's price selector.
                Cache(window).Clear();
                MarketCache(window).Clear();
                var sorted = (List<ThingDef>)sort.Invoke(null,
                    new object[] { defs, new Func<ThingDef, string>(d => d.label), quote, direction });
                bool monotonic = true;
                for (int i = 1; i < sorted.Count; i++)
                    if (direction == 1 ? quote(sorted[i - 1]) > quote(sorted[i]) : quote(sorted[i - 1]) < quote(sorted[i]))
                        monotonic = false;
                // Vanilla trait weapons seed their traits from each new Thing's
                // identity. Fresh samples may have different values; a cached
                // sample and every non-trait good must retain their own quotes.
                var currentPrices = defs.ToDictionary(d => d, quote);
                string changed = string.Join(", ", prices.Where(pair => !HasGeneratedTraits(pair.Key) && quote(pair.Key) != pair.Value)
                    .Select(pair => pair.Key.defName + ":" + pair.Value + "->" + quote(pair.Key)));
                check("Cold catalogue price sort " + direction + " covers every item and preserves its quote"
                    + " (count=" + sorted.Count + ", monotonic=" + monotonic + ", unexpected changes=" + changed + ")",
                    sorted.Count == defs.Count && !sorted.Contains(node) && monotonic
                    && changed.Length == 0 && currentPrices.All(pair => quote(pair.Key) == pair.Value));
            }

            MethodInfo matches = typeof(Window_CorporateComms).GetMethod("MatchesCatalog", InstanceFields);
            List<ThingDef> searchResults = defs.Where(d => (bool)matches.Invoke(window,
                new object[] { d, d.LabelCap.ToString(), node.LabelCap.ToString(), 0 })).ToList();
            check("Searching for the Cerebrex node does not reintroduce excluded goods",
                searchResults.Count == 0 && quote(node) == 0);

            CheckPreviewLifecycle(window, node, check);
            check("Catalogue, search, preview replacement and close preserve every shared destroyable flag",
                originalFlags.All(pair => pair.Key.destroyable == pair.Value));
            check("Preview replacement and close leave corporate custody unchanged", SameVault(network, initialVault));
        }
        finally { window.PostClose(); }

        CheckLargeCatalogBudget(context, check);

        CheckRejectedOrder(network, context, node, check);
        CheckLegacyStock(network, context, node, check);
        CheckLegacyEscrow(network, context, node, check);
        check("Catalogue, preview and legacy custody cleanup preserve shared Def and global destruction rules",
            originalFlags.All(pair => pair.Key.destroyable == pair.Value)
            && Equals(originalOverride, destructionOverride?.GetValue(null)));
    }

    private static void CheckLargeCatalogBudget(CorporateTradeContext context, Action<string, bool> check)
    {
        const int count = 50000;
        var source = Enumerable.Repeat(ThingDefOf.Steel, count).ToList();
        var window = new Window_CorporateComms(context);
        MethodInfo update = typeof(Window_CorporateComms).GetMethod("UpdateOrderCatalogWork", InstanceFields);
        MethodInfo refresh = typeof(Window_CorporateComms).GetMethod("RequestOrderViewRefresh", InstanceFields);
        MethodInfo visible = typeof(Window_CorporateComms).GetMethod("CatalogVisibleRange",
            BindingFlags.Static | BindingFlags.NonPublic);
        try
        {
            Set(window, "selectedPage", 2);
            Set(window, "requestedPage", 2);
            Set(window, "orderCatalogSource", source);

            update.Invoke(window, null);
            int firstPass = Get<int>(window, "orderIndexPosition");
            check("A 50,000-item catalogue is not indexed in one UI update",
                firstPass > 0 && firstPass < count && !Get<bool>(window, "orderIndexComplete"));

            bool nameReady = AdvanceUntilSettled(window, update, 5000);
            check("A 50,000-item name catalogue completes through bounded update slices",
                nameReady && CollectionCount(window, "orderView") == count);

            Set(window, "orderSort", 1);
            refresh.Invoke(window, null);
            update.Invoke(window, null);
            check("A 50,000-item price sort keeps its previous stable snapshot while work is pending",
                !IsSettled(window) && CollectionCount(window, "orderView") == count);
            bool priceReady = AdvanceUntilSettled(window, update, 5000);
            check("A 50,000-item price sort completes without a synchronous full-list UI pass",
                priceReady && CollectionCount(window, "orderView") == count);

            object[] range = { count, 4, 173f, 750000f, 520f, 0, 0 };
            visible.Invoke(null, range);
            int first = (int)range[5];
            int last = (int)range[6];
            check("A 50,000-item grid only submits visible rows and a small buffer for drawing",
                first >= 0 && last <= count && last > first && last - first <= 24);
        }
        finally { window.PostClose(); }
    }

    private static bool AdvanceUntilSettled(Window_CorporateComms window, MethodInfo update, int maximumPasses)
    {
        for (int i = 0; i < maximumPasses && !IsSettled(window); i++) update.Invoke(window, null);
        return IsSettled(window);
    }

    private static bool IsSettled(Window_CorporateComms window)
        => Get<bool>(window, "orderIndexComplete") && Get<object>(window, "orderViewTask") == null
            && Get<int>(window, "orderViewCommittedVersion") == Get<int>(window, "orderViewVersion");

    private static int CollectionCount(Window_CorporateComms window, string field)
        => ((ICollection)Get<object>(window, field)).Count;

    private static void CheckPreviewLifecycle(Window_CorporateComms window, ThingDef node, Action<string, bool> check)
    {
        Set(window, "orderStuff", null);
        Set(window, "orderQuality", QualityCategory.Normal);
        SelectPreview(window, ThingDefOf.Steel);
        Thing first = Preview(window);
        check("Allowed catalogue selection creates a real unspawned, unowned preview",
            first != null && first.def == ThingDefOf.Steel && !first.Spawned && first.holdingOwner == null && !first.Destroyed);
        SelectPreview(window, ThingDefOf.ComponentSpacer);
        Thing second = Preview(window);
        check("Changing between allowed goods destroys the discarded ordinary preview",
            second != null && second != first && first.Destroyed);
        SelectPreview(window, node);
        check("A stale protected selection clears its former preview without manufacturing a Cerebrex node",
            Preview(window) == null && second.Destroyed && !node.destroyable);

        // Represent an already existing preview from a terminal opened before the fix.
        Thing legacy = CorporateNetwork.MakeProduct(node, null, QualityCategory.Normal);
        Set(window, "orderPreview", legacy);
        SelectPreview(window, ThingDefOf.Steel);
        Thing ordinary = Preview(window);
        check("Replacing a legacy protected preview releases its reference without attempting destruction",
            ordinary != null && ordinary.def == ThingDefOf.Steel && legacy.holdingOwner == null && !legacy.Destroyed);
        window.PostClose();
        check("Window close destroys ordinary previews and clears preview and quote cache",
            Preview(window) == null && Cache(window).Count == 0 && ordinary.Destroyed);
        Set(window, "orderPreview", legacy);
        window.PostClose();
        check("Window close safely clears a legacy protected preview", Preview(window) == null
            && !legacy.Destroyed && legacy.holdingOwner == null && !node.destroyable);
    }

    private static void CheckRejectedOrder(CorporateNetwork network, CorporateTradeContext context, ThingDef node,
        Action<string, bool> check)
    {
        context.Invalidate();
        int originalSilver = context.SilverCount;
        Thing[] initialVault = network.Vault.ToArray();
        int originalOrders = network.Orders.Count;
        bool rejected = !network.PlaceOrder(context, node, null, QualityCategory.Normal, 1, out string reason);
        check("Direct Cerebrex order is rejected without debit, receipt or orphaned custody",
            rejected && !string.IsNullOrEmpty(reason) && context.SilverCount == originalSilver
            && network.Orders.Count == originalOrders && SameVault(network, initialVault) && !node.destroyable);
    }

    private static void CheckLegacyStock(CorporateNetwork network, CorporateTradeContext context, ThingDef node,
        Action<string, bool> check)
    {
        network.EnsureWeeklyOffers();
        CorporateStock offer = network.Stock.First();
        Thing originalSample = offer.sample;
        int originalPrice = offer.unitPrice;
        int originalCount = offer.count;
        int originalOrders = network.Orders.Count;
        int originalSilver = context.SilverCount;
        Thing[] initialVault = network.Vault.ToArray();
        Thing legacy = CorporateNetwork.MakeProduct(node, null, QualityCategory.Normal);
        network.Vault.TryAdd(legacy, false);
        try
        {
            offer.sample = legacy;
            offer.unitPrice = 1;
            offer.count = 1;
            bool rejected = !network.BuyStock(context, offer, 1, out string reason);
            check("A legacy weekly Cerebrex offer cannot bypass catalogue restrictions",
                rejected && !string.IsNullOrEmpty(reason) && context.SilverCount == originalSilver
                && network.Orders.Count == originalOrders && offer.count == 1
                && legacy.holdingOwner == network.Vault && !legacy.Destroyed);
        }
        finally
        {
            offer.sample = originalSample;
            offer.unitPrice = originalPrice;
            offer.count = originalCount;
            CorporateNetwork.DiscardUnspawnedProduct(legacy);
        }
        check("Legacy protected stock cleanup detaches custody without destroying its sample",
            legacy.holdingOwner == null && !legacy.Destroyed && SameVault(network, initialVault));
    }

    private static void CheckLegacyEscrow(CorporateNetwork network, CorporateTradeContext context, ThingDef node,
        Action<string, bool> check)
    {
        Thing[] initialVault = network.Vault.ToArray();
        Thing legacy = CorporateNetwork.MakeProduct(node, null, QualityCategory.Normal);
        // SaveOrder recreates a historical receipt directly; the current PlaceOrder gate remains enforced.
        CorporateOrder order = (CorporateOrder)typeof(CorporateNetwork).GetMethod("SaveOrder", InstanceFields).Invoke(network,
            new object[] { node, 1, 0, CorporateNetwork.Now + CorporateNetwork.DayTicks, new List<Thing> { legacy } });
        check("Legacy protected escrow fixture is held by the actual corporate vault", legacy.holdingOwner == network.Vault);
        bool cancelled = network.CancelOrder(context, order, out string reason);
        check("Cancelling legacy Cerebrex escrow removes custody and clears its references: " + (reason ?? "ok"),
            cancelled && order.state == CorporateOrderState.Cancelled && order.goods.Count == 0
            && legacy.holdingOwner == null && !legacy.Destroyed && SameVault(network, initialVault) && !node.destroyable);
    }

    private static bool SameVault(CorporateNetwork network, Thing[] expected)
        => network.Vault.Count == expected.Length && expected.All(t => network.Vault.Contains(t));

    private static bool HasGeneratedTraits(ThingDef def) => def.comps != null && def.comps.Any(c =>
        typeof(CompBladelinkWeapon).IsAssignableFrom(c.compClass) || typeof(CompUniqueWeapon).IsAssignableFrom(c.compClass));

    private static Dictionary<ThingDef, int> Cache(Window_CorporateComms window)
        => (Dictionary<ThingDef, int>)typeof(Window_CorporateComms).GetField("catalogPrices", InstanceFields).GetValue(window);

    private static Dictionary<ThingDef, float> MarketCache(Window_CorporateComms window)
        => (Dictionary<ThingDef, float>)typeof(Window_CorporateComms).GetField("catalogMarketValues", InstanceFields).GetValue(window);

    private static Thing Preview(Window_CorporateComms window)
        => (Thing)typeof(Window_CorporateComms).GetField("orderPreview", InstanceFields).GetValue(window);

    private static T Get<T>(Window_CorporateComms window, string field)
        => (T)typeof(Window_CorporateComms).GetField(field, InstanceFields).GetValue(window);

    private static void Set(Window_CorporateComms window, string field, object value)
        => typeof(Window_CorporateComms).GetField(field, InstanceFields).SetValue(window, value);

    private static void SelectPreview(Window_CorporateComms window, ThingDef def)
    {
        Set(window, "orderDef", def);
        typeof(Window_CorporateComms).GetMethod("RefreshOrderPreview", InstanceFields).Invoke(window, null);
    }
}
