using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public enum CorporateOrderState { Preparing, Ready, Completed, Cancelled, RefundDue, Refunded }

    public sealed class CorporateStock : IExposable
    {
        public int id;
        public Thing sample;
        public int count;
        public int unitPrice;
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_References.Look(ref sample, "sample");
            Scribe_Values.Look(ref count, "count", 0);
            Scribe_Values.Look(ref unitPrice, "unitPrice", 0);
        }
    }

    public sealed class CorporateOrder : IExposable
    {
        public int id;
        public ThingDef def;
        public string savedLabel;
        public int quantity;
        public int deliveredQuantity;
        public int creditedTurnover;
        public int paid;
        public int createdTick;
        public int readyTick;
        public CorporateOrderSpeed speed;
        public int expediteFee;
        public CorporateOrderState state;
        public List<Thing> goods = new List<Thing>();
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref savedLabel, "savedLabel", null);
            Scribe_Values.Look(ref quantity, "quantity", 0);
            Scribe_Values.Look(ref deliveredQuantity, "deliveredQuantity", 0);
            Scribe_Values.Look(ref creditedTurnover, "creditedTurnover", -1);
            Scribe_Values.Look(ref paid, "paid", 0);
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            Scribe_Values.Look(ref readyTick, "readyTick", 0);
            // 旧合同默认普通备货，付款与原到货 tick 始终保留。
            Scribe_Values.Look(ref speed, "speed", CorporateOrderSpeed.Standard);
            Scribe_Values.Look(ref expediteFee, "expediteFee", 0);
            Scribe_Values.Look(ref state, "state", CorporateOrderState.Preparing);
            Scribe_Collections.Look(ref goods, "goods", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && goods == null) goods = new List<Thing>();
            // Legacy partial deliveries predate the ledger; only later deliveries should count.
            if (Scribe.mode == LoadSaveMode.PostLoadInit && creditedTurnover < 0)
                creditedTurnover = quantity <= 0 ? 0 : (int)((long)paid * Math.Min(quantity, deliveredQuantity) / quantity);
        }
    }

    public partial class CorporateNetwork
    {
        private const string CombatApparelBuyOnlyTag = "Mugirl_CombatApparelBuyOnly";
        private List<CorporateStock> stock = new List<CorporateStock>();
        private List<CorporateOrder> orders = new List<CorporateOrder>();
        // 每局组件内只缓存 Def 目录，不保存 UI 预览物品；新档/读档组件重新构造。
        private List<ThingDef> orderCatalog;
        public IReadOnlyList<CorporateStock> Stock => stock;
        public IReadOnlyList<CorporateOrder> Orders => orders;
        public CorporateTradeSettingsDef TradeSettings => CorporateTradeDefOf.Mugirl_CorporateTradeSettings;
        public IReadOnlyList<ThingDef> OrderCatalog
        {
            get
            {
                PrepareOrderCatalog();
                return orderCatalog;
            }
        }

        // 在读档/开局长事件中预建一次完整目录，避免首次打开终端时同步扫描大型模组列表。
        private void PrepareOrderCatalog()
        {
            if (orderCatalog != null) return;
            orderCatalog = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(IsOrderable).OrderBy(d => d.label).ToList();
            // 全局来源和转换关系只在开局/读档时预建一次。
            _ = SupplyAnalysis;
            // 未售出的旧版随机特化武器样品改用当前规格；已签约货物保持原样。
            foreach (CorporateStock offer in stock)
            {
                Thing old = offer.sample;
                if (old == null || old.Destroyed || old.TryGetComp<CompUniqueWeapon>() == null
                    || !CanAutomaticallySupply(old.def)) continue;
                if (!old.TryGetQuality(out QualityCategory quality)) quality = QualityCategory.Normal;
                if (!TryMakeGoods(old.def, old.Stuff, quality, 1, out List<Thing> samples)) continue;
                offer.sample = samples[0];
                vault.TryAdd(offer.sample, false);
                if (!orders.Any(o => o.goods.Contains(old))) DiscardUnspawnedProduct(old);
            }
        }

        public bool IsOrderable(ThingDef def)
        {
            if (def == null || def.category != ThingCategory.Item || def == ThingDefOf.Silver
                || def.tradeability == Tradeability.None || def.BaseMarketValue <= 0f
                || !def.destroyable || def.destroyOnDrop || def.isUnfinishedThing
                || def.tradeTags?.Contains(CombatApparelBuyOnlyTag) == true) return false;
            Type type = def.thingClass;
            // 需要专用生成内容的对象不通过通用工厂制造空壳；常规物资/装备均可订购。
            return type == typeof(Thing) || type == typeof(ThingWithComps)
                || type == typeof(Medicine) || typeof(Apparel).IsAssignableFrom(type);
        }

        public float ProductFactor(Thing thing)
        {
            if (thing == null || thing.Destroyed || !thing.def.destroyable || thing.def.category != ThingCategory.Item || thing.IsNotFresh()
                || (thing is Apparel apparel && apparel.WornByCorpse) || CompBiocodable.IsBiocoded(thing)) return 0f;
            if (thing.def == Mugirl_DefOf.Mugirl_Milk || thing.def == CorporateTradeDefOf.Mugirl_Wool)
                return TradeSettings.rawProductPriceFactor;
            // 已卸载家具作为 MinifiedThing 交货；材料和建造分类都属于内部 Building。
            if (thing is MinifiedThing mini && mini.InnerThing is Building furniture
                && !furniture.Destroyed && furniture.def.category == ThingCategory.Building
                && furniture.def.minifiedDef == mini.def
                && furniture.def.designationCategory == CorporateTradeDefOf.Furniture
                && furniture.Stuff == CorporateTradeDefOf.Mugirl_Wool)
                return TradeSettings.productPriceFactor;
            if (thing.Stuff == CorporateTradeDefOf.Mugirl_Wool || TradeSettings.productDefs.Contains(thing.def))
                return TradeSettings.productPriceFactor;
            CompIngredients ingredients = thing.TryGetComp<CompIngredients>();
            return ingredients?.ingredients?.Contains(Mugirl_DefOf.Mugirl_Milk) == true
                ? TradeSettings.productPriceFactor : 0f;
        }

        public int ProductQuote(Thing thing, int quantity)
        {
            if (quantity <= 0 || thing == null || quantity > thing.stackCount) return 0;
            return Price(thing.MarketValue * ProductFactor(thing), quantity, false);
        }

        public bool SellProduct(CorporateTradeContext context, Thing source, int quantity, out string reason)
        {
            if (!CanTrade(context, out reason)) return false;
            int value = ProductQuote(source, quantity);
            if (value <= 0 || !context.AvailableContractThings.Contains(source))
            { reason = "Mugirl.Corporate.GoodsChanged".Translate(); return false; }
            if (!context.TryTakeContractThing(source, quantity, out Thing taken))
            { reason = "Mugirl.Corporate.GoodsChanged".Translate(); return false; }
            vault.TryAdd(taken, false);
            if (!context.DeliverSilver(value))
            {
                if (!context.Deliver(taken))
                {
                    CorporateOrder recovery = SaveOrder(taken.def, taken.stackCount, 0, Now, new List<Thing> { taken });
                    recovery.state = CorporateOrderState.Ready;
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

        public static int Price(float unitValue, int quantity, bool roundUp = true)
        {
            if (quantity <= 0 || float.IsNaN(unitValue) || float.IsInfinity(unitValue) || unitValue <= 0f || unitValue > 100000000f) return 0;
            return MoneyTotal((decimal)unitValue * quantity, roundUp);
        }

        public static Thing MakeProduct(ThingDef def, ThingDef stuff, QualityCategory quality)
        {
            Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? stuff ?? GenStuff.DefaultStuffFor(def) : null);
            thing.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Outsider);
            return thing;
        }

        // Only temporary manufactured goods and corporate escrow may use this path.
        // Protected legacy samples must leave custody without invoking Destroy or
        // changing the shared Def/global destruction rules.
        internal static void DiscardUnspawnedProduct(Thing thing)
        {
            if (thing == null || thing.Destroyed) return;
            if (thing.Spawned || thing is Pawn)
                throw new InvalidOperationException("Corporate product cleanup requires an unspawned item.");
            if (thing.def.destroyable) thing.Destroy(DestroyMode.Vanish);
            else thing.holdingOwner?.Remove(thing);
        }

        private bool TryMakeGoods(ThingDef def, ThingDef stuff, QualityCategory quality, int quantity, out List<Thing> goods)
        {
            goods = new List<Thing>();
            try
            {
                int left = quantity;
                while (left > 0)
                {
                    Thing thing = MakeOrderProduct(def, stuff, quality);
                    thing.stackCount = Math.Min(left, Math.Max(1, def.stackLimit));
                    left -= thing.stackCount;
                    goods.Add(thing);
                }
                return true;
            }
            catch (Exception ex)
            {
                foreach (Thing thing in goods) DiscardUnspawnedProduct(thing);
                goods.Clear();
                MugirlLog.WarningOnce("Corporate.MakeProduct." + def.defName,
                    "Mugirl.Corporate.GenerationError".Translate(def.LabelCap, ex.Message));
                return false;
            }
        }

        public bool BuyStock(CorporateTradeContext context, CorporateStock offer, int quantity, out string reason)
        {
            if (!CanTrade(context, out reason)) return false;
            EnsureWeeklyOffers();
            if (offer == null || !stock.Contains(offer) || offer.sample == null || offer.sample.Destroyed
                || !CanAutomaticallySupply(offer.sample.def)
                || quantity <= 0 || quantity > offer.count)
            { reason = "Mugirl.Corporate.GoodsChanged".Translate(); return false; }
            int cost = StockQuote(offer, quantity);
            QualityCategory quality;
            if (!offer.sample.TryGetQuality(out quality)) quality = QualityCategory.Normal;
            if (cost <= 0 || context.SilverCount < cost)
            { reason = "Mugirl.Corporate.InsufficientSilver".Translate(cost); return false; }
            if (!TryMakeGoods(offer.sample.def, offer.sample.Stuff, quality, quantity, out List<Thing> goods))
            { reason = "Mugirl.Corporate.CannotMake".Translate(); return false; }
            if (!context.TrySpendSilver(cost))
            {
                foreach (Thing thing in goods) DiscardUnspawnedProduct(thing);
                reason = "Mugirl.Corporate.InsufficientSilver".Translate(cost); return false;
            }
            offer.count -= quantity;
            // 即时交易也写入交付收据，交付失败仍可在订单页免费领取。
            CorporateOrder receipt = SaveOrder(offer.sample.def, quantity, cost, Now, goods);
            receipt.state = CorporateOrderState.Ready;
            ClaimOrder(context, receipt, out reason);
            reason = null;
            return true;
        }

        public bool PlaceOrder(CorporateTradeContext context, ThingDef def, ThingDef stuff, QualityCategory quality,
            int quantity, out string reason, CorporateOrderSpeed speed = CorporateOrderSpeed.Standard)
        {
            if (!CanTrade(context, out reason)) return false;
            if (!ValidOrderSpeed(speed))
            { reason = "Mugirl.Corporate.InvalidOrder".Translate(); return false; }
            if (IsOrderable(def) && !CanAutomaticallySupply(def))
            { reason = "Mugirl.CorporatePricing.Restricted".Translate(); return false; }
            if (!IsOrderable(def) || quantity <= 0 || quantity > 2500 || quality > QualityCategory.Excellent
                || (def.MadeFromStuff && (stuff == null || !GenStuff.AllowedStuffsFor(def).Contains(stuff))))
            { reason = "Mugirl.Corporate.InvalidOrder".Translate(); return false; }
            if (orders.Count(o => o.state == CorporateOrderState.Preparing || o.state == CorporateOrderState.Ready) >= TradeSettings.maxActiveOrders)
            { reason = "Mugirl.Corporate.OrderLimit".Translate(TradeSettings.maxActiveOrders); return false; }
            // 先用同规格样品检查上限，超额订单不生成整批货物，也不扣款。
            if (!TryMakeGoods(def, stuff, quality, 1, out List<Thing> goods))
            { reason = "Mugirl.Corporate.CannotMake".Translate(); return false; }
            int maximum = MaxOrderQuantity(goods[0]);
            if (quantity > maximum)
            {
                foreach (Thing thing in goods) DiscardUnspawnedProduct(thing);
                reason = "Mugirl.CorporatePricing.QuantityLimitExceeded".Translate(maximum);
                return false;
            }
            if (quantity > 1)
            {
                foreach (Thing thing in goods) DiscardUnspawnedProduct(thing);
                if (!TryMakeGoods(def, stuff, quality, quantity, out goods))
                { reason = "Mugirl.Corporate.CannotMake".Translate(); return false; }
            }
            int standardCost = OrderQuote(goods[0], quantity);
            int cost = ExpeditedTotal(standardCost, speed);
            if (cost <= 0 || !context.TrySpendSilver(cost))
            {
                foreach (Thing thing in goods) DiscardUnspawnedProduct(thing);
                reason = "Mugirl.Corporate.InsufficientSilver".Translate(cost); return false;
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
                if (thing.holdingOwner != vault) vault.TryAdd(thing, false);
            CorporateOrder order = new CorporateOrder { id = NewId(), def = def, savedLabel = def.LabelCap,
                quantity = quantity, paid = cost, createdTick = Now, readyTick = due, goods = goods };
            orders.Add(order);
            if (orders.Count > 200)
            {
                CorporateOrder oldestClosed = orders.FirstOrDefault(o => o.state == CorporateOrderState.Completed
                    || o.state == CorporateOrderState.Cancelled || o.state == CorporateOrderState.Refunded);
                if (oldestClosed != null) orders.Remove(oldestClosed);
            }
            Record("Mugirl.Corporate.RecordPurchase", order.savedLabel + " ×" + quantity, -cost);
            return order;
        }

        public bool ClaimOrder(CorporateTradeContext context, CorporateOrder order, out string reason)
        {
            reason = "Mugirl.Corporate.DeliveryUnavailable".Translate();
            if (context == null || !context.IsValid || !Unlocked || !orders.Contains(order)) return false;
            TradeTick();
            if (order.state == CorporateOrderState.RefundDue)
            {
                int refund = order.quantity <= 0 ? order.paid
                    : (int)((long)order.paid * Math.Max(0, order.quantity - order.deliveredQuantity) / order.quantity);
                if (!context.DeliverSilver(refund)) return false;
                order.state = CorporateOrderState.Refunded;
                DestroyGoods(order);
                Record("Mugirl.Corporate.RecordRefund", order.savedLabel, refund);
                reason = null;
                return true;
            }
            if (order.state != CorporateOrderState.Ready) return false;
            for (int i = order.goods.Count - 1; i >= 0; i--)
            {
                Thing thing = order.goods[i];
                if (thing == null || thing.Destroyed || thing.holdingOwner != vault) continue;
                int delivered = thing.stackCount;
                if (context.Deliver(thing))
                {
                    order.deliveredQuantity += delivered;
                    CreditOrderTurnover(order);
                    order.goods.RemoveAt(i);
                }
            }
            if (order.goods.Count > 0) return false;
            order.state = CorporateOrderState.Completed;
            reason = null;
            return true;
        }

        public bool CancelOrder(CorporateTradeContext context, CorporateOrder order, out string reason)
        {
            reason = "Mugirl.Corporate.CannotCancel".Translate();
            if (context == null || !context.IsValid || !Unlocked || !orders.Contains(order)
                || order.state != CorporateOrderState.Preparing || Now >= order.readyTick
                || Now - order.createdTick > TradeSettings.cancelWindowDays * DayTicks) return false;
            int refund = Mathf.FloorToInt(order.paid * TradeSettings.cancelRefundFactor);
            if (!context.DeliverSilver(refund)) return false;
            order.state = CorporateOrderState.Cancelled;
            DestroyGoods(order);
            Record("Mugirl.Corporate.RecordRefund", order.savedLabel, refund);
            reason = null;
            return true;
        }

        private static void DestroyGoods(CorporateOrder order)
        {
            foreach (Thing thing in order.goods)
                if (thing != null && !thing.Destroyed && thing.holdingOwner == Current?.Vault) DiscardUnspawnedProduct(thing);
            order.goods.Clear();
        }

        partial void TradeRefreshWeekly()
        {
            foreach (CorporateStock previous in stock)
                DiscardUnspawnedProduct(previous.sample);
            stock.Clear();
            int weeklyCount = WeeklyStockCount;
            List<ThingDef> candidates = TradeSettings.stapleDefs.Where(d => CanAutomaticallySupply(d) && !IsSpecialOrder(d)).Distinct().ToList();
            List<ThingDef> supplements = OrderCatalog.Where(d => (ServiceLevel >= 3
                    ? d.BaseMarketValue < 2500f && d.techLevel <= TechLevel.Spacer
                    : d.BaseMarketValue < 500f && d.techLevel <= TechLevel.Industrial)
                && CanAutomaticallySupply(d) && !IsSpecialOrder(d) && !candidates.Contains(d)).InRandomOrder().Take(weeklyCount).ToList();
            candidates = candidates.InRandomOrder().Take(Math.Max(1, weeklyCount - 4)).Concat(supplements)
                .Take(weeklyCount).ToList();
            foreach (ThingDef def in candidates)
            {
                if (!TryMakeGoods(def, GenStuff.DefaultStuffFor(def), QualityCategory.Normal, 1, out List<Thing> samples)) continue;
                Thing sample = samples[0];
                vault.TryAdd(sample, false);
                int count = def.stackLimit == 1 ? Rand.RangeInclusive(1, 3)
                    : def.BaseMarketValue <= 5f ? Rand.RangeInclusive(100, 600) : Rand.RangeInclusive(10, 60);
                var offer = new CorporateStock { id = NewId(), sample = sample, count = count };
                offer.unitPrice = StockUnitPrice(offer);
                if (offer.unitPrice <= 0) { DiscardUnspawnedProduct(sample); continue; }
                stock.Add(offer);
            }
        }

        partial void TradeTick()
        {
            foreach (CorporateOrder order in orders)
            {
                if (order.state != CorporateOrderState.Preparing && order.state != CorporateOrderState.Ready) continue;
                if (order.def == null || order.goods.Any(t => t == null || t.Destroyed))
                { order.state = CorporateOrderState.RefundDue; continue; }
                if (order.state == CorporateOrderState.Preparing && Now >= order.readyTick)
                {
                    order.state = CorporateOrderState.Ready;
                    MugirlGameUtility.Letters.ReceiveLetter("Mugirl.Corporate.OrderArrivedTitle".Translate(),
                        "Mugirl.Corporate.OrderArrivedText".Translate(order.savedLabel, order.quantity), LetterDefOf.PositiveEvent);
                }
            }
        }

        partial void TradeExposeData()
        {
            Scribe_Collections.Look(ref stock, "corporateStock", LookMode.Deep);
            Scribe_Collections.Look(ref orders, "corporateOrders", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (stock == null) stock = new List<CorporateStock>();
                if (orders == null) orders = new List<CorporateOrder>();
                orderCatalog = null;
                supplyAnalysis = null;
            }
        }
    }
}
