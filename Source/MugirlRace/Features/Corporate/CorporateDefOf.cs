namespace Mugirl.Features.Corporate;

// 巨企功能的 Def。
[DefOf]
internal static class CorporateDefOf
{
    public static CorporateFinanceConfigDef Mugirl_CorporateFinanceConfig = null!;

    public static CorporateIntroductionConfigDef Mugirl_CorporateIntroductionConfig = null!;

    public static QuestScriptDef Mugirl_CorporateIntroductionQuest = null!;

    public static JobDef Mugirl_CorporateUseComms = null!;

    public static JobDef Mugirl_CorporateTalkRepresentative = null!;

    public static CorporatePeopleConfigDef Mugirl_CorporatePeopleConfig = null!;

    public static ThoughtDef Mugirl_CorporateSoldMugirl = null!;

    public static TraderKindDef Mugirl_CorporatePersonnelTrader = null!;

    public static CorporateMissionConfigDef Mugirl_CorporateMissionConfig = null!;

    public static SitePartDef Mugirl_CorporatePurgeSite = null!;

    public static SitePartDef Mugirl_CorporateResearchSite = null!;

    public static PrefabDef Mugirl_CorporateFusionResearchSite = null!;

    public static QuestScriptDef Mugirl_CorporateMission = null!;

    // 原版 Core 内容，原版 DefOf 未收录：转介的贸易请求任务与研究站守卫兵种。
    public static QuestScriptDef TradeRequest = null!;

    public static PawnKindDef Mercenary_Gunner = null!;

    public static DesignationCategoryDef Furniture = null!;

    public static CorporateTradeConfigDef Mugirl_CorporateTradeConfig = null!;

    // 巨企敌对派系引用。
    public static FactionDef Mugirl_GiantCorporation = null!;

    public static PawnKindDef Mugirl_GiantCorp_Courier = null!;

    public static PawnKindDef Mugirl_CorporateRepresentative = null!;

    public static PawnKindDef Mugirl_CorporateSupport = null!;

    public static PawnKindDef Mugirl_CorporateShowcase = null!;

    // 巨企支援小队死战条款特性引用。
    public static TraitDef Mugirl_CorporateDiehard = null!;

    // 巨企支援小队搜敌 duty 引用（含空闲医疗节点）。
    public static Verse.AI.DutyDef Mugirl_CorporateSupportHunt = null!;

    public static JobDef Mugirl_CorporateSupportTend = null!;

    static CorporateDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(CorporateDefOf));
}
