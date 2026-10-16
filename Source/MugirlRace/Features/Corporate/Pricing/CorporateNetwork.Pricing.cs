using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Corporate;

// decimal 定价引擎：订购条款、备货期、兑现价值保护、现货与订购报价，以及按规格固定的订购样品工厂。
public partial class CorporateNetwork
{
    private static CorporateSupplyAnalysis SupplyAnalysis => CorporateSupplyAnalysis.Shared;

    public bool CanAutomaticallySupply(ThingDef? def) => IsOrderable(def) && !SupplyAnalysis.Get(def).restricted;

    public string OrderPricingReason(ThingDef? def)
    {
        CorporateSupplyAnalysis.Entry entry = SupplyAnalysis.Get(def);
        if (entry.restricted)
        {
            return "Mugirl.CorporatePricing.Restricted".Translate();
        }

        if (entry.redemption > 0m)
        {
            return "Mugirl.CorporatePricing.Redemption".Translate();
        }

        if (entry.natural)
        {
            return "Mugirl.CorporatePricing.Natural".Translate();
        }

        return IsSpecialOrder(def) ? "Mugirl.CorporatePricing.Sourced".Translate() : "Mugirl.CorporatePricing.Market".Translate();
    }

    // 稀缺只延长备货期；不能由 Sellable、基础市值或按名称维护的名单触发高价。
    public bool IsSpecialOrder([NotNullWhen(true)] ThingDef? def)
    {
        if (def == null)
        {
            return false;
        }

        CorporateSupplyAnalysis.Entry entry = SupplyAnalysis.Get(def);
        return !entry.natural && !entry.sourced && !entry.crafted;
    }

    public bool IsRareOrder([NotNullWhen(true)] ThingDef? def) => def != null && SupplyAnalysis.Get(def).tech >= TechLevel.Spacer;

    private void OrderTerms(ThingDef def, out float factor, out IntRange days)
    {
        CorporateTradeConfigDef settings = TradeSettings;
        factor = settings.orderPriceFactor;
        days = settings.orderDays;
        TechLevel tech = SupplyAnalysis.Get(def).tech;
        if (tech >= TechLevel.Spacer)
        {
            factor = UnityMathf.Max(factor, settings.rareOrderPriceFactor);
            days = settings.rareOrderDays;
        }

        if (tech >= TechLevel.Archotech)
        {
            factor = UnityMathf.Max(factor, settings.archotechOrderPriceFactor);
            days = settings.archotechOrderDays;
        }

        if (IsSpecialOrder(def))
        {
            days = new IntRange(Math.Max(days.min, settings.specialOrderDays.min), Math.Max(days.max, settings.specialOrderDays.max));
        }
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

    private static bool ValidOrderSpeed(CorporateOrderSpeed speed) => CorporateMoney.IsValidSpeed(speed);

    public static int PreparationTicks(int days, CorporateOrderSpeed speed) => CorporateMoney.PreparationTicks(days, speed, DayTicks);

    public static int ExpeditedTotal(int standardTotal, CorporateOrderSpeed speed) => CorporateMoney.ExpeditedTotal(standardTotal, speed);

    private decimal ProtectedUnitValue(ThingDef def, float marketValue, decimal factor, bool discount)
    {
        CorporateSupplyAnalysis.Entry entry = SupplyAnalysis.Get(def);
        decimal unit = CorporateMoney.Value(marketValue) * factor * (discount ? (decimal)OrderDiscountFactor : 1m);
        // 缺失估价才用 500 兜底；有明确低市值的副产物不会因无商人出售被抬价。
        if (entry.missingValue)
        {
            unit = Math.Max(unit, TradeSettings.unsourcedMinimumUnitPrice);
        }

        return Math.Max(unit, entry.redemption * (decimal)TradeSettings.redemptionPriceFactor);
    }

    public int OrderQuote(Thing? preview, int quantity, CorporateOrderSpeed speed = CorporateOrderSpeed.Standard)
    {
        if (preview == null || preview.Destroyed)
        {
            return 0;
        }

        return OrderQuote(preview.def, preview.MarketValue, quantity, speed);
    }

    internal int StandardOrderUnitPrice(ThingDef? def, float marketValue)
    {
        if (def == null || marketValue <= 0f || float.IsNaN(marketValue) || float.IsInfinity(marketValue))
        {
            return 0;
        }

        OrderTerms(def, out float factor, out _);
        // 数量分档依据未折扣、未加急的同规格单价，保留兑现价值保护。
        return CorporateMoney.Total(ProtectedUnitValue(def, marketValue, (decimal)factor, false));
    }

    public int MaxOrderQuantity(Thing? preview) => preview == null || preview.Destroyed ? 0
        : MaxOrderQuantity(preview.def, preview.MarketValue);

    internal int MaxOrderQuantity(ThingDef? def, float marketValue)
    {
        if (def == null)
        {
            return 0;
        }

        if (def.stackLimit <= 1)
        {
            return 10;
        }

        int standardPrice = StandardOrderUnitPrice(def, marketValue);
        return standardPrice > 0 && standardPrice < TradeSettings.highValueOrderThreshold ? 2500 : 10;
    }

    public string OrderQuantityLimitReason(Thing preview)
    {
        int maximum = MaxOrderQuantity(preview);
        if (preview.def.stackLimit <= 1)
        {
            return "Mugirl.CorporatePricing.QuantityLimitSingle".Translate(maximum);
        }

        return maximum == 10
            ? "Mugirl.CorporatePricing.QuantityLimitHighValue".Translate(TradeSettings.highValueOrderThreshold.ToString("N0"), maximum)
            : "Mugirl.CorporatePricing.QuantityLimitBulk".Translate(maximum);
    }

    internal int OrderQuote(ThingDef? def, float marketValue, int quantity, CorporateOrderSpeed speed = CorporateOrderSpeed.Standard)
    {
        if (def == null || marketValue <= 0f || quantity <= 0 || !ValidOrderSpeed(speed))
        {
            return 0;
        }

        OrderTerms(def, out float factor, out _);
        int standard = CorporateMoney.Total(ProtectedUnitValue(def, marketValue, (decimal)factor, true) * quantity);
        return ExpeditedTotal(standard, speed);
    }

    public int StockUnitPrice(CorporateStockOffer? offer) => StockQuote(offer, 1);

    public int StockQuote(CorporateStockOffer? offer, int quantity)
    {
        return quantity <= 0 || !TryStockUnitValue(offer, out decimal unit) ? 0 : CorporateMoney.Total(unit * quantity);
    }

    // 现货单价只依赖样品，界面每帧求可购数量时只计算一次市值与收购系数。
    public int AffordableStockQuantity(CorporateStockOffer? offer, int silver)
    {
        return TryStockUnitValue(offer, out decimal unit) ? CorporateMoney.AffordableQuantity(unit, offer.count, silver) : 0;
    }

    private bool TryStockUnitValue([NotNullWhen(true)] CorporateStockOffer? offer, out decimal unit)
    {
        unit = 0m;
        if (offer?.sample is not { Destroyed: false } sample)
        {
            return false;
        }

        decimal factor = Math.Max((decimal)TradeSettings.stockPriceFactor, (decimal)ProductFactor(sample) + 0.15m);
        unit = ProtectedUnitValue(sample.def, sample.MarketValue, factor, false);
        if (IsRareOrder(sample.def))
        {
            OrderTerms(sample.def, out float orderFactor, out _);
            unit = Math.Max(unit, ProtectedUnitValue(sample.def, sample.MarketValue, (decimal)orderFactor, true));
        }

        return true;
    }

    // 同一 Def、材料与品质使用同一种规格；预览和实际交付共享，且不推进殖民地随机序列。
    public static Thing MakeOrderProduct(ThingDef def, ThingDef? stuff, QualityCategory quality)
        => CorporateProductFactory.MakeOrderProduct(def, stuff, quality);

    /// <summary>独特武器词条的随机种子：巨企订购样品生成期间改用规格种子，其余情况保持原版种子。</summary>
    internal static int ResolveProductSeed(int vanillaSeed) => CorporateProductFactory.ResolveProductSeed(vanillaSeed);
}
