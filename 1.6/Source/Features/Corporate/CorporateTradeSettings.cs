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
        public float rareOrderPriceFactor = 2.8f;
        public IntRange orderDays = new IntRange(3, 5);
        public IntRange rareOrderDays = new IntRange(7, 12);
        public int maxActiveOrders = 5;
        public int cancelWindowDays = 1;
        public float cancelRefundFactor = 0.8f;
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
