using RimWorld;
using Verse;

namespace Mugirl
{
    [DefOf]
    public static class MugirlContentDefOf
    {
        // 雪牛娘战斗员服装引用。
        public static ThingDef Mugirl_Combatant_BulletproofVest;
        public static ThingDef Mugirl_Combatant_Clothes;
        public static ThingDef Mugirl_Combatant_Helmet;
        public static ThingDef Mugirl_Combatant_Underwear;
        public static ThingDef Mugirl_SpeciallyStockings;
        public static ThingDef Mugirl_MilitaryDress;
        public static ThingDef Mugirl_MilitaryUniform;
        public static ThingDef Mugirl_Bikini;
        public static ThingDef Mugirl_Bikini_Stocking;

        // 高科技束具引用。
        public static ThingDef Mugirl_ShockCollar;

        // PMC 敌对单位服装引用。
        public static ThingDef PMC_Helmet;
        public static ThingDef PMC_CaptainHelmet;
        public static ThingDef PMC_CombatUniform;

        // 雪牛奶制食品引用。
        public static ThingDef Mugirl_MilkPudding;
        public static ThingDef Mugirl_Cheese;
        public static ThingDef Mugirl_AgedCheese;
        public static ThingDef Mugirl_BaotaSugar;
        public static ThingDef Mugirl_MilkCandy;
        public static ThingDef Mugirl_CowCake;
        public static ThingDef Mugirl_MilkPowder;
        public static ThingDef Mugirl_MilkTablet;
        public static ThingDef Mugirl_MilkCreamApple;

        // 快递事件特殊物品引用。
        public static ThingDef Mugirl_CourierDiary;

        // 巨企敌对派系引用。
        public static FactionDef Mugirl_GiantCorporations_Hostile;

        // 巨企事件 pawnKind 引用。
        public static PawnKindDef AI_GC_Grunts;
        public static PawnKindDef AI_GC_Courier;
        public static PawnKindDef AI_GC_Soldiers;
        public static PawnKindDef AI_GC_Elites;
        public static PawnKindDef AI_GC_MugirlRaider;
        public static PawnKindDef Mugirl_CorporateRepresentative;
        public static PawnKindDef Mugirl_CorporateSupport;

        // 巨企支援小队死战条款特性引用。
        public static TraitDef Mugirl_CorporateDiehard;

        // 巨企支援小队搜敌 duty 引用（含空闲医疗节点）。
        public static Verse.AI.DutyDef Mugirl_CorporateSupportHunt;

        // 快递事件任务引用。
        public static QuestScriptDef Mugirl_CourierRaid;

        // 新增雪牛娘事件引用。
        [DefAlias("Mugirl_Migration")]
        public static IncidentDef Mugirl_MigrationIncident;
        [DefAlias("Mugirl_FusionInvestment")]
        public static IncidentDef Mugirl_FusionInvestmentIncident;
        [DefAlias("Mugirl_RunawayFarmQuest")]
        public static IncidentDef Mugirl_RunawayFarmQuestIncident;
        public static QuestScriptDef Mugirl_RunawayFarmQuest;
        public static SitePartDef Mugirl_RunawayFarm;
        public static HediffDef Mugirl_RunawayPanic;
        public static ThingDef Mugirl_PunisherRace;
        public static PawnKindDef Mugirl_Punisher;
        public static HediffDef Mugirl_PunisherKnockout;
        public static FleckDef ShockwaveFast;

        // 快递事件交互 Job 引用。
        public static JobDef Mugirl_ReadCourierDiary;
        public static JobDef Mugirl_TalkCourier;
        public static JobDef Mugirl_TalkFusionInvestor;

        // 奶制食品效果引用。
        public static HediffDef Hediff_MugirlAgedCheese;
        public static HediffDef Hediff_MugirlCowCake;
        public static HediffDef Hediff_MugirlMilkPowder;
        public static HediffDef Hediff_MugirlMilkTablet;

        // 奶制食品心情引用。
        public static ThoughtDef Consumed_MugirlPudding;
        public static ThoughtDef Consumed_MugirlCheese;
        public static ThoughtDef Consumed_MugirlAgedCheese;
        public static ThoughtDef Consumed_MugirlRichMilkFlavor;
        public static ThoughtDef Consumed_MugirlMilkCandy;

        // 奶制食品配方引用。
        public static RecipeDef Mugirl_MakeMilkPudding;
        public static RecipeDef Mugirl_MakeCheese;
        public static RecipeDef Mugirl_MakeAgedCheese;
        public static RecipeDef Mugirl_MakeBaotaSugar;
        public static RecipeDef Mugirl_MakeMilkCandy;
        public static RecipeDef Mugirl_MakeCowCake;
        public static RecipeDef Mugirl_MakeMilkPowder;
        public static RecipeDef Mugirl_MakeMilkTablet;
        public static RecipeDef Mugirl_MakeMilkCreamApple;

        // 快递事件战利品使用既有钥匙 Def，不改 defName 以保护数值与掉落配置。
        public static ThingDef Mugirl_SlaveApparelKey_Medieval;
        public static ThingDef Mugirl_SlaveApparelKey_Industrial;
    }
}
