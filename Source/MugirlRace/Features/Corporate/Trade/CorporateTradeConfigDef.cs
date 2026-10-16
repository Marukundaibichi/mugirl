namespace Mugirl.Features.Corporate;

// 巨企交易参数，XML 见 1.7/Defs/Misc/CorporateTradeSettings.xml。
public class CorporateTradeConfigDef : Def
{
    public int weeklyStockCount = 16;
    public float rawProductPriceFactor = 1.6f;
    public float productPriceFactor = 1.3f;
    public float stockPriceFactor = 1.65f;
    public float orderPriceFactor = 2.2f;
    public float rareOrderPriceFactor = 3f;
    public float archotechOrderPriceFactor = 4.5f;
    public int unsourcedMinimumUnitPrice = 500;
    public float redemptionPriceFactor = 1.2f;
    public IntRange orderDays = new IntRange(3, 5);
    public IntRange rareOrderDays = new IntRange(12, 20);
    public IntRange archotechOrderDays = new IntRange(20, 35);
    public IntRange specialOrderDays = new IntRange(30, 60);
    public int maxActiveOrders = 5;
    public int highValueOrderThreshold = 1000;
    public int cancelWindowDays = 1;
    public float cancelRefundFactor = 0.8f;
    public List<ThingDef> productDefs = new List<ThingDef>();
    public List<ThingDef> stapleDefs = new List<ThingDef>();
}
