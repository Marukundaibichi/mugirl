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
        public float rareOrderPriceFactor = 3f;
        public float archotechOrderPriceFactor = 4.5f;
        // 保留旧字段供旧补丁读取；新版不再按物品名单或 Sellable 设置特供倍率、底价。
        public float specialOrderPriceFactor = 1f;
        public int specialOrderMinimumUnitPrice = 0;
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
        public List<ThingDef> rareOrderDefs = new List<ThingDef>();
        public List<ThingDef> specialOrderDefs = new List<ThingDef>();
        public List<ThingDef> productDefs = new List<ThingDef>();
        public List<ThingDef> stapleDefs = new List<ThingDef>();
    }

    [DefOf]
    public static class CorporateTradeDefOf
    {
        public static ThingDef Mugirl_Wool;
        public static DesignationCategoryDef Furniture;
        public static CorporateTradeSettingsDef Mugirl_CorporateTradeSettings;
    }
}
