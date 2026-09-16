using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Mugirl
{
    public class CorporateTradeSettingsDef : Def
    {
        public int weeklyStockCount = 16;
        public float rawProductPriceFactor = 1.6f;
        public float productPriceFactor = 1.3f;
        public float stockPriceFactor = 1.65f;
        public float orderPriceFactor = 2.2f;
        public float rareOrderPriceFactor = 6f;
        public float archotechOrderPriceFactor = 10f;
        public float specialOrderPriceFactor = 20f;
        public int specialOrderMinimumUnitPrice = 20000;
        public IntRange orderDays = new IntRange(3, 5);
        public IntRange rareOrderDays = new IntRange(12, 20);
        public IntRange archotechOrderDays = new IntRange(20, 35);
        public IntRange specialOrderDays = new IntRange(30, 60);
        public int maxActiveOrders = 5;
        public int cancelWindowDays = 1;
        public float cancelRefundFactor = 0.8f;
        public List<ThingDef> rareOrderDefs = new List<ThingDef>();
        public List<ThingDef> specialOrderDefs = new List<ThingDef>();
        public List<ThingDef> productDefs = new List<ThingDef>();
        public List<ThingDef> stapleDefs = new List<ThingDef>();
    }

    [DefOf]
    public static class CorporateTradeDefOf
    {
        public static ThingDef Mugirl_Wool;
        public static CorporateTradeSettingsDef Mugirl_CorporateTradeSettings;
    }
}
