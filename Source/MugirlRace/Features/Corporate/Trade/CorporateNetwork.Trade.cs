using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Mugirl.Features.Milk;

namespace Mugirl.Features.Corporate;

// 现货与订购：订购目录、产品回收、货物工厂、退款、每周现货刷新与订单 tick。
public partial class CorporateNetwork
{
    private bool tradeRead;

    private const string CombatApparelBuyOnlyTag = "Mugirl_CombatApparelBuyOnly";

    // StaticCacheLifecycle: 进程级，只依赖 Def 与当前语言的标签；Def 重新加载（切换语言）时由 CorporateDefCache 重建。
    private static readonly CorporateDefCache<List<ThingDef>> OrderCatalogCache = new(BuildOrderCatalog);

    public IReadOnlyList<CorporateStockOffer> Stock => trade.stock;
    public IReadOnlyList<CorporateOrder> Orders => trade.orders;
    public CorporateTradeConfigDef TradeSettings => CorporateDefOf.Mugirl_CorporateTradeConfig;
    public IReadOnlyList<ThingDef> OrderCatalog => OrderCatalogCache.Value;

    // 读档已开户的存档时，在长事件中预建目录与来源索引，避免首次打开终端时同步扫描大型模组列表。
    // 其余情况由首次使用（开户后的每周刷新、终端页面）触发同一惰性缓存。
    private static void PrepareOrderCatalog()
    {
        _ = OrderCatalogCache.Value;
        _ = SupplyAnalysis;
    }

    // 按标签的文化排序；中文标签在 1.7 走 ICU，较慢，所以整个进程只排序一次。
    private static List<ThingDef> BuildOrderCatalog()
    {
        return DefDatabase<ThingDef>.AllDefsListForReading.Where(IsOrderableDef)
            .OrderBy(d => d.label, StringComparer.CurrentCulture).ToList();
    }

    // 每次加载重新生成可供应的特化武器展示样品；已签约货物保持原样。
    private void RefreshStockSamples()
    {
        foreach (CorporateStockOffer offer in trade.stock)
        {
            Thing? old = offer.sample;
            if (old == null || old.Destroyed || old.TryGetComp<CompUniqueWeapon>() == null
                || !CanAutomaticallySupply(old.def))
            {
                continue;
            }

            if (!old.TryGetQuality(out QualityCategory quality))
            {
                quality = QualityCategory.Normal;
            }

            if (!TryMakeGoods(old.def, old.Stuff, quality, 1, out List<Thing> samples))
            {
                continue;
            }

            Thing sample = samples[0];
            offer.sample = sample;
            vault.TryAdd(sample, false);
            if (!trade.orders.Any(o => o.goods.Contains(old)))
            {
                DiscardUnspawnedProduct(old);
            }
        }
    }

    public bool IsOrderable([NotNullWhen(true)] ThingDef? def) => IsOrderableDef(def);

    private static bool IsOrderableDef([NotNullWhen(true)] ThingDef? def)
    {
        if (def == null || def.category != ThingCategory.Item || def == ThingDefOf.Silver
            || def.tradeability == Tradeability.None || def.BaseMarketValue <= 0f
            || !def.destroyable || def.destroyOnDrop || def.isUnfinishedThing
            || def.tradeTags?.Contains(CombatApparelBuyOnlyTag) == true)
        {
            return false;
        }

        Type type = def.thingClass;
        // 需要专用生成内容的对象不通过通用工厂制造空壳；常规物资/装备均可订购。
        return type == typeof(Thing) || type == typeof(ThingWithComps)
            || type == typeof(Medicine) || typeof(Apparel).IsAssignableFrom(type);
    }

    public float ProductFactor(Thing? thing)
    {
        if (thing == null || thing.Destroyed || !thing.def.destroyable || thing.def.category != ThingCategory.Item || thing.IsNotFresh()
            || (thing is Apparel apparel && apparel.WornByCorpse) || CompBiocodable.IsBiocoded(thing))
        {
            return 0f;
        }

        CorporateTradeConfigDef settings = TradeSettings;
        if (thing.def == MilkDefOf.Mugirl_Milk || thing.def == MilkDefOf.Mugirl_Wool)
        {
            return settings.rawProductPriceFactor;
        }

        // 已卸载家具作为 MinifiedThing 交货；材料和建造分类都属于内部 Building。
        if (thing is MinifiedThing mini && mini.InnerThing is Building furniture
            && !furniture.Destroyed && furniture.def.category == ThingCategory.Building
            && furniture.def.minifiedDef == mini.def
            && furniture.def.designationCategory == CorporateDefOf.Furniture
            && furniture.Stuff == MilkDefOf.Mugirl_Wool)
        {
            return settings.productPriceFactor;
        }

        if (thing.Stuff == MilkDefOf.Mugirl_Wool || settings.productDefs.Contains(thing.def))
        {
            return settings.productPriceFactor;
        }

        CompIngredients? ingredients = thing.TryGetComp<CompIngredients>();
        return ingredients?.ingredients?.Contains(MilkDefOf.Mugirl_Milk) == true
            ? settings.productPriceFactor : 0f;
    }

    public int ProductQuote(Thing? thing, int quantity)
    {
        if (quantity <= 0 || thing == null || quantity > thing.stackCount)
        {
            return 0;
        }

        return Price(thing.MarketValue * ProductFactor(thing), quantity, false);
    }

    public bool SellProduct(CorporateTradeContext context, Thing source, int quantity, [NotNullWhen(false)] out string? reason)
    {
        if (!CanTrade(context, out reason))
        {
            return false;
        }

        int value = ProductQuote(source, quantity);
        if (value <= 0 || !context.AvailableContractThings.Contains(source)
            || !context.TryTakeContractThing(source, quantity, out Thing? taken))
        {
            reason = "Mugirl.Corporate.GoodsChanged".Translate();
            return false;
        }

        vault.TryAdd(taken, false);
        if (!context.DeliverSilver(value))
        {
            if (!context.Deliver(taken))
            {
                SaveRecoveryOrder(taken);
            }

            reason = "Mugirl.Corporate.DeliveryUnavailable".Translate();
            return false;
        }

        string label = taken.LabelCap;
        taken.Destroy();
        Record("Mugirl.Corporate.RecordSale", label, value);
        AddTradeTurnover(value);
        reason = null;
        return true;
    }

    // 无法立即交还的物品以免费的“已到货”收据保存，玩家可在订单页领取。
    internal void SaveRecoveryOrder(Thing thing)
    {
        CorporateOrder recovery = SaveOrder(thing.def, thing.stackCount, 0, Now, new List<Thing> { thing });
        recovery.state = CorporateOrderState.Ready;
    }

    public static int Price(float unitValue, int quantity, bool roundUp = true) => CorporateMoney.Price(unitValue, quantity, roundUp);

    public static Thing MakeProduct(ThingDef def, ThingDef? stuff, QualityCategory quality) => CorporateProductFactory.MakeProduct(def, stuff, quality);

    // 只用于临时生产的货物和巨企托管物。受保护的样品必须在不调用 Destroy、
    // 也不改变共享 Def 或全局销毁规则的情况下离开托管。
    internal static void DiscardUnspawnedProduct(Thing? thing) => CorporateProductFactory.DiscardUnspawnedProduct(thing);

    private static void DiscardUnspawnedProducts(List<Thing> things) => CorporateProductFactory.DiscardUnspawnedProducts(things);

    private static bool TryMakeGoods(ThingDef def, ThingDef? stuff, QualityCategory quality, int quantity, out List<Thing> goods)
        => CorporateProductFactory.TryMakeGoods(def, stuff, quality, quantity, out goods);

    public bool BuyStock(CorporateTradeContext context, CorporateStockOffer offer, int quantity, [NotNullWhen(false)] out string? reason)
    {
        if (!CanTrade(context, out reason))
        {
            return false;
        }

        EnsureWeeklyOffers();
        if (offer == null || !trade.stock.Contains(offer) || offer.sample is not { Destroyed: false } sample
            || !CanAutomaticallySupply(sample.def)
            || quantity <= 0 || quantity > offer.count)
        {
            reason = "Mugirl.Corporate.GoodsChanged".Translate();
            return false;
        }

        int cost = StockQuote(offer, quantity);
        if (!sample.TryGetQuality(out QualityCategory quality))
        {
            quality = QualityCategory.Normal;
        }

        if (cost <= 0 || context.SilverCount < cost)
        {
            reason = "Mugirl.Corporate.InsufficientSilver".Translate(cost);
            return false;
        }

        if (!TryMakeGoods(sample.def, sample.Stuff, quality, quantity, out List<Thing> goods))
        {
            reason = "Mugirl.Corporate.CannotMake".Translate();
            return false;
        }

        if (!context.TrySpendSilver(cost))
        {
            DiscardUnspawnedProducts(goods);
            reason = "Mugirl.Corporate.InsufficientSilver".Translate(cost);
            return false;
        }

        offer.count -= quantity;
        // 即时交易也写入交付收据，交付失败仍可在订单页免费领取。
        CorporateOrder receipt = SaveOrder(sample.def, quantity, cost, Now, goods);
        receipt.state = CorporateOrderState.Ready;
        ClaimOrder(context, receipt, out _);
        reason = null;
        return true;
    }

    public bool PlaceOrder(CorporateTradeContext context, ThingDef def, ThingDef? stuff, QualityCategory quality,
        int quantity, [NotNullWhen(false)] out string? reason, CorporateOrderSpeed speed = CorporateOrderSpeed.Standard)
    {
        if (!CanTrade(context, out reason))
        {
            return false;
        }

        if (!ValidOrderSpeed(speed))
        {
            reason = "Mugirl.Corporate.InvalidOrder".Translate();
            return false;
        }

        if (IsOrderable(def) && !CanAutomaticallySupply(def))
        {
            reason = "Mugirl.CorporatePricing.Restricted".Translate();
            return false;
        }

        if (!IsOrderable(def) || quantity <= 0 || quantity > 2500 || quality > QualityCategory.Excellent
            || (def.MadeFromStuff && (stuff == null || !GenStuff.AllowedStuffsFor(def).Contains(stuff))))
        {
            reason = "Mugirl.Corporate.InvalidOrder".Translate();
            return false;
        }

        if (trade.orders.Count(o => o.state == CorporateOrderState.Preparing || o.state == CorporateOrderState.Ready) >= TradeSettings.maxActiveOrders)
        {
            reason = "Mugirl.Corporate.OrderLimit".Translate(TradeSettings.maxActiveOrders);
            return false;
        }

        // 先用同规格样品检查上限，超额订单不生成整批货物，也不扣款。
        if (!TryMakeGoods(def, stuff, quality, 1, out List<Thing> goods))
        {
            reason = "Mugirl.Corporate.CannotMake".Translate();
            return false;
        }

        int maximum = MaxOrderQuantity(goods[0]);
        if (quantity > maximum)
        {
            DiscardUnspawnedProducts(goods);
            reason = "Mugirl.CorporatePricing.QuantityLimitExceeded".Translate(maximum);
            return false;
        }

        if (quantity > 1)
        {
            DiscardUnspawnedProducts(goods);
            if (!TryMakeGoods(def, stuff, quality, quantity, out goods))
            {
                reason = "Mugirl.Corporate.CannotMake".Translate();
                return false;
            }
        }

        int standardCost = OrderQuote(goods[0], quantity);
        int cost = ExpeditedTotal(standardCost, speed);
        if (cost <= 0 || !context.TrySpendSilver(cost))
        {
            DiscardUnspawnedProducts(goods);
            reason = "Mugirl.Corporate.InsufficientSilver".Translate(cost);
            return false;
        }

        IntRange days = OrderLeadTime(def);
        long due = (long)Now + PreparationTicks(days.RandomInRange, speed);
        CorporateOrder order = SaveOrder(def, quantity, cost, (int)Math.Min(int.MaxValue, due), goods);
        order.speed = speed;
        order.expediteFee = cost - standardCost;
        reason = null;
        return true;
    }

    private CorporateOrder SaveOrder(ThingDef def, int quantity, int cost, int due, List<Thing> goods)
    {
        foreach (Thing thing in goods)
        {
            if (thing.holdingOwner != vault)
            {
                vault.TryAdd(thing, false);
            }
        }

        CorporateOrder order = new CorporateOrder
        {
            id = NewId(),
            def = def,
            savedLabel = def.LabelCap,
            quantity = quantity,
            paid = cost,
            createdTick = Now,
            readyTick = due,
            goods = new List<Thing?>(goods),
        };
        trade.orders.Add(order);
        if (trade.orders.Count > 200)
        {
            CorporateOrder? oldestClosed = trade.orders.Find(o => o.state == CorporateOrderState.Completed
                || o.state == CorporateOrderState.Cancelled || o.state == CorporateOrderState.Refunded);
            if (oldestClosed != null)
            {
                trade.orders.Remove(oldestClosed);
            }
        }

        Record("Mugirl.Corporate.RecordPurchase", order.savedLabel + " ×" + quantity, -cost);
        return order;
    }

    public bool ClaimOrder(CorporateTradeContext? context, CorporateOrder order, [NotNullWhen(false)] out string? reason)
    {
        reason = "Mugirl.Corporate.DeliveryUnavailable".Translate();
        if (context == null || !context.IsValid || !Unlocked || !trade.orders.Contains(order))
        {
            return false;
        }

        TradeTick();
        if (order.state == CorporateOrderState.RefundDue)
        {
            int refund = order.quantity <= 0 ? order.paid
                : (int)((long)order.paid * Math.Max(0, order.quantity - order.deliveredQuantity) / order.quantity);
            if (!context.DeliverSilver(refund))
            {
                return false;
            }

            order.state = CorporateOrderState.Refunded;
            DestroyGoods(order);
            Record("Mugirl.Corporate.RecordRefund", order.savedLabel, refund);
            reason = null;
            return true;
        }

        if (order.state != CorporateOrderState.Ready)
        {
            return false;
        }

        for (int i = order.goods.Count - 1; i >= 0; i--)
        {
            Thing? thing = order.goods[i];
            if (thing == null || thing.Destroyed || thing.holdingOwner != vault)
            {
                continue;
            }

            int delivered = thing.stackCount;
            if (context.Deliver(thing))
            {
                order.deliveredQuantity += delivered;
                CreditOrderTurnover(order);
                order.goods.RemoveAt(i);
            }
        }

        if (order.goods.Count > 0)
        {
            return false;
        }

        order.state = CorporateOrderState.Completed;
        reason = null;
        return true;
    }

    public bool CancelOrder(CorporateTradeContext? context, CorporateOrder order, [NotNullWhen(false)] out string? reason)
    {
        reason = "Mugirl.Corporate.CannotCancel".Translate();
        int now = Now;
        if (context == null || !context.IsValid || !Unlocked || !trade.orders.Contains(order)
            || order.state != CorporateOrderState.Preparing || now >= order.readyTick
            || now - order.createdTick > TradeSettings.cancelWindowDays * DayTicks)
        {
            return false;
        }

        // 退款按付款额的 decimal 精确份额向下取整，不经 float 乘法。
        int refund = CorporateMoney.FloorShare(order.paid, TradeSettings.cancelRefundFactor);
        if (!context.DeliverSilver(refund))
        {
            return false;
        }

        order.state = CorporateOrderState.Cancelled;
        DestroyGoods(order);
        Record("Mugirl.Corporate.RecordRefund", order.savedLabel, refund);
        reason = null;
        return true;
    }

    private void DestroyGoods(CorporateOrder order)
    {
        foreach (Thing? thing in order.goods)
        {
            if (thing != null && !thing.Destroyed && thing.holdingOwner == vault)
            {
                DiscardUnspawnedProduct(thing);
            }
        }

        order.goods.Clear();
    }

    private void TradeRefreshWeekly()
    {
        foreach (CorporateStockOffer previous in trade.stock)
        {
            DiscardUnspawnedProduct(previous.sample);
        }

        trade.stock.Clear();
        int weeklyCount = WeeklyStockCount;
        bool premium = ServiceLevel >= 3;
        List<ThingDef> candidates = TradeSettings.stapleDefs.Where(d => CanAutomaticallySupply(d) && !IsSpecialOrder(d)).Distinct().ToList();
        List<ThingDef> supplements = OrderCatalog.Where(d => (premium
                ? d.BaseMarketValue < 2500f && d.techLevel <= TechLevel.Spacer
                : d.BaseMarketValue < 500f && d.techLevel <= TechLevel.Industrial)
            && CanAutomaticallySupply(d) && !IsSpecialOrder(d) && !candidates.Contains(d)).InRandomOrder().Take(weeklyCount).ToList();
        candidates = candidates.InRandomOrder().Take(Math.Max(1, weeklyCount - 4)).Concat(supplements)
            .Take(weeklyCount).ToList();
        foreach (ThingDef def in candidates)
        {
            if (!TryMakeGoods(def, GenStuff.DefaultStuffFor(def), QualityCategory.Normal, 1, out List<Thing> samples))
            {
                continue;
            }

            Thing sample = samples[0];
            vault.TryAdd(sample, false);
            int count = def.stackLimit == 1 ? Rand.RangeInclusive(1, 3)
                : def.BaseMarketValue <= 5f ? Rand.RangeInclusive(100, 600) : Rand.RangeInclusive(10, 60);
            var offer = new CorporateStockOffer { id = NewId(), sample = sample, count = count };
            offer.unitPrice = StockUnitPrice(offer);
            if (offer.unitPrice <= 0)
            {
                DiscardUnspawnedProduct(sample);
                continue;
            }

            trade.stock.Add(offer);
        }
    }

    private void TradeTick()
    {
        int now = Now;
        foreach (CorporateOrder order in trade.orders)
        {
            if (order.state != CorporateOrderState.Preparing && order.state != CorporateOrderState.Ready)
            {
                continue;
            }

            if (order.def == null || order.HasLostGoods)
            {
                order.state = CorporateOrderState.RefundDue;
                continue;
            }

            if (order.state == CorporateOrderState.Preparing && now >= order.readyTick)
            {
                order.state = CorporateOrderState.Ready;
                GameUtility.Letters.ReceiveLetter("Mugirl.Corporate.OrderArrivedTitle".Translate(),
                    "Mugirl.Corporate.OrderArrivedText".Translate(order.savedLabel, order.quantity), LetterDefOf.PositiveEvent);
            }
        }
    }

    private void TradeExposeData()
    {
        CorporateStateScribe.Look(trade, "trade", ref tradeRead);
    }
}
