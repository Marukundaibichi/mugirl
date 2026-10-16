namespace Mugirl.Features.Corporate;

// 委托内容名单。每次调用新建数组：Def 在切换语言时会整体重建，不在静态字段中缓存 Def 引用。
internal static class CorporateMissionContent
{
    // 分销合同的商品。
    internal static ThingDef[] DistributionProducts =>
        [ThingDefOf.ComponentIndustrial, ThingDefOf.MedicineIndustrial, ThingDefOf.MealSurvivalPack, ThingDefOf.Beer];

    // 可转介的原版任务。
    internal static QuestScriptDef[] NativeQuests => [CorporateDefOf.TradeRequest, QuestScriptDefOf.OpportunitySite_ItemStash];

    // 核聚变研究站的三名守卫。
    internal static PawnKindDef Guard => CorporateDefOf.Mercenary_Gunner;
}
