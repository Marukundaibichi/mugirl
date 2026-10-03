using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    public enum CorporateOrderSpeed { Standard, Express, Priority }

    public partial class CorporateNetwork
    {
        // StaticCacheLifecycle: 仅当前线程创建订购样品期间有效；finally 恢复，不能跨生成或跨档残留。
        [ThreadStatic] internal static int? orderProductSeed;
        private CorporateSupplyAnalysis supplyAnalysis;
        private CorporateSupplyAnalysis SupplyAnalysis => supplyAnalysis ?? (supplyAnalysis = new CorporateSupplyAnalysis(TradeSettings));

        public bool CanAutomaticallySupply(ThingDef def) => IsOrderable(def) && !SupplyAnalysis.Get(def).restricted;

        public string OrderPricingReason(ThingDef def)
        {
            CorporateSupplyAnalysis.Entry entry = SupplyAnalysis.Get(def);
            return ("Mugirl.CorporatePricing." + (entry.restricted ? "Restricted"
                : entry.redemption > 0m ? "Redemption" : entry.natural ? "Natural"
                : IsSpecialOrder(def) ? "Sourced" : "Market")).Translate();
        }

        // 稀缺只延长备货期；不能由 Sellable、基础市值或按名称维护的名单触发高价。
        public bool IsSpecialOrder(ThingDef def)
        {
            if (def == null) return false;
            CorporateSupplyAnalysis.Entry entry = SupplyAnalysis.Get(def);
            return !entry.natural && !entry.sourced && !entry.crafted;
        }

        public bool IsRareOrder(ThingDef def) => def != null && SupplyAnalysis.Get(def).tech >= TechLevel.Spacer;

        private void OrderTerms(ThingDef def, out float factor, out IntRange days)
        {
            factor = TradeSettings.orderPriceFactor;
            days = TradeSettings.orderDays;
            TechLevel tech = SupplyAnalysis.Get(def).tech;
            if (tech >= TechLevel.Spacer)
            { factor = Math.Max(factor, TradeSettings.rareOrderPriceFactor); days = TradeSettings.rareOrderDays; }
            if (tech >= TechLevel.Archotech)
            { factor = Math.Max(factor, TradeSettings.archotechOrderPriceFactor); days = TradeSettings.archotechOrderDays; }
            if (IsSpecialOrder(def))
                days = new IntRange(Math.Max(days.min, TradeSettings.specialOrderDays.min), Math.Max(days.max, TradeSettings.specialOrderDays.max));
        }

        public IntRange OrderLeadTime(ThingDef def)
        {
            OrderTerms(def, out _, out IntRange days);
            return days;
        }

        public IntRange OrderLeadTicks(ThingDef def, CorporateOrderSpeed speed)
        {
            IntRange days = OrderLeadTime(def);
            return new IntRange(PreparationTicks(days.min, speed), PreparationTicks(days.max, speed));
        }

        private static bool ValidOrderSpeed(CorporateOrderSpeed speed) => speed >= CorporateOrderSpeed.Standard && speed <= CorporateOrderSpeed.Priority;

        public static int PreparationTicks(int days, CorporateOrderSpeed speed)
        {
            if (!ValidOrderSpeed(speed) || days <= 0) return 0;
            long ticks = (long)days * DayTicks;
            int divisor = speed == CorporateOrderSpeed.Express ? 2 : speed == CorporateOrderSpeed.Priority ? 4 : 1;
            return (int)Math.Min(int.MaxValue, Math.Max(1L, (ticks + divisor - 1) / divisor));
        }

        public static int ExpeditedTotal(int standardTotal, CorporateOrderSpeed speed)
        {
            if (standardTotal <= 0 || !ValidOrderSpeed(speed)) return 0;
            decimal factor = speed == CorporateOrderSpeed.Express ? 1.5m : speed == CorporateOrderSpeed.Priority ? 2m : 1m;
            return MoneyTotal(standardTotal * factor);
        }

        private static int MoneyTotal(decimal total, bool roundUp = true)
        {
            if (total <= 0m || total > 100000000m) return 0;
            return (int)(roundUp ? decimal.Ceiling(total) : decimal.Floor(total));
        }

        private decimal ProtectedUnitValue(ThingDef def, float marketValue, decimal factor, bool discount)
        {
            CorporateSupplyAnalysis.Entry entry = SupplyAnalysis.Get(def);
            decimal unit = CorporateSupplyAnalysis.MoneyValue(marketValue) * factor * (discount ? (decimal)OrderDiscountFactor : 1m);
            // 缺失估价才用 500 兜底；有明确低市值的副产物不会因无商人出售被抬价。
            if (entry.missingValue) unit = Math.Max(unit, TradeSettings.unsourcedMinimumUnitPrice);
            unit = Math.Max(unit, entry.redemption * (decimal)TradeSettings.redemptionPriceFactor);
            return unit;
        }

        public int OrderQuote(Thing preview, int quantity, CorporateOrderSpeed speed = CorporateOrderSpeed.Standard)
        {
            if (preview == null || preview.Destroyed) return 0;
            return OrderQuote(preview.def, preview.MarketValue, quantity, speed);
        }

        internal int OrderQuote(ThingDef def, float marketValue, int quantity, CorporateOrderSpeed speed = CorporateOrderSpeed.Standard)
        {
            if (def == null || marketValue <= 0f || quantity <= 0 || !ValidOrderSpeed(speed)) return 0;
            OrderTerms(def, out float factor, out _);
            int standard = MoneyTotal(ProtectedUnitValue(def, marketValue, (decimal)factor, true) * quantity);
            return ExpeditedTotal(standard, speed);
        }

        public int StockUnitPrice(CorporateStock offer) => StockQuote(offer, 1);

        public int StockQuote(CorporateStock offer, int quantity)
        {
            if (offer?.sample == null || offer.sample.Destroyed || quantity <= 0) return 0;
            Thing sample = offer.sample;
            decimal factor = Math.Max((decimal)TradeSettings.stockPriceFactor, (decimal)ProductFactor(sample) + 0.15m);
            decimal price = ProtectedUnitValue(sample.def, sample.MarketValue, factor, false);
            if (IsRareOrder(sample.def))
            {
                OrderTerms(sample.def, out float orderFactor, out _);
                price = Math.Max(price, ProtectedUnitValue(sample.def, sample.MarketValue, (decimal)orderFactor, true));
            }
            return MoneyTotal(price * quantity);
        }

        public int AffordableStockQuantity(CorporateStock offer, int silver)
        {
            int low = 0, high = Math.Max(0, offer?.count ?? 0);
            while (low < high)
            {
                int middle = low + (high - low + 1) / 2;
                int quote = StockQuote(offer, middle);
                if (quote > 0 && quote <= silver) low = middle;
                else high = middle - 1;
            }
            return low;
        }

        // 同一 Def、材料与品质使用同一种规格；预览和实际交付共享，且不推进殖民地随机序列。
        public static Thing MakeOrderProduct(ThingDef def, ThingDef stuff, QualityCategory quality)
        {
            int seed = Gen.HashCombineInt(def.shortHash, (stuff?.shortHash ?? 0) * 7 + (int)quality);
            int? previous = orderProductSeed;
            try
            {
                orderProductSeed = seed;
                using (Rand.Block(seed)) return MakeProduct(def, stuff, quality);
            }
            finally { orderProductSeed = previous; }
        }
    }

    // 原版词条生成器内部以 ThingID 再次播种；只在巨企生成期间使用规格种子。
    // 沿用原版 1–3 词条、互斥校验和权重规则，名称/颜色/能力初始化仍由原版完成。
    [HarmonyPatch(typeof(CompUniqueWeapon), "InitializeTraits")]
    internal static class Harmony_CorporateWeaponSpecification
    {
        private static bool Prefix(CompUniqueWeapon __instance)
        {
            if (!CorporateNetwork.orderProductSeed.HasValue) return true;
            using (Rand.Block(CorporateNetwork.orderProductSeed.Value))
            {
                int count = Rand.RangeInclusive(1, 3);
                for (int i = 0; i < count; i++)
                {
                    var options = DefDatabase<WeaponTraitDef>.AllDefs.Where(__instance.CanAddTrait).ToList();
                    if (options.Count > 0) __instance.AddTrait(options.RandomElementByWeight(t => t.commonality));
                }
                __instance.Setup(false);
            }
            return false;
        }
    }
}
