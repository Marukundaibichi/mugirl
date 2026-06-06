using RimWorld;
using Verse;

namespace MooGirl
{
    [DefOf]
    public static class MooGirlContentDefOf
    {
        // 雪牛娘战斗员服装引用。
        public static ThingDef MooGirl_Combatant_BulletproofVest;
        public static ThingDef MooGirl_Combatant_Clothes;
        public static ThingDef MooGirl_Combatant_Helmet;
        public static ThingDef MooGirl_Combatant_Underwear;
        public static ThingDef MooGirl_SpeciallyStockings;
        public static ThingDef MooGirl_MilitaryDress;
        public static ThingDef MooGirl_MilitaryUniform;

        // PMC 敌对单位服装引用。
        public static ThingDef PMC_Helmet;
        public static ThingDef PMC_CaptainHelmet;
        public static ThingDef PMC_CombatUniform;

        // 雪牛奶制食品引用。
        public static ThingDef MooGirl_MilkPudding;
        public static ThingDef MooGirl_Cheese;
        public static ThingDef MooGirl_AgedCheese;
        public static ThingDef MooGirl_BaotaSugar;
        public static ThingDef MooGirl_MilkCandy;
        public static ThingDef MooGirl_CowCake;
        public static ThingDef MooGirl_MilkPowder;
        public static ThingDef MooGirl_MilkTablet;
        public static ThingDef MooGirl_MilkCreamApple;

        // 快递事件特殊物品引用。
        public static ThingDef MooGirl_CourierDiary;

        // 巨企敌对派系引用。
        public static FactionDef MooGirl_GiantCorporations_Hostile;

        // 巨企事件 pawnKind 引用。
        public static PawnKindDef AI_GC_Grunts;
        public static PawnKindDef AI_GC_Courier;
        public static PawnKindDef AI_GC_Soldiers;
        public static PawnKindDef AI_GC_Elites;
        public static PawnKindDef AI_GC_MooGirlRaider;

        // 快递事件任务引用。
        public static QuestScriptDef MooGirl_CourierRaid;

        // 快递事件交互 Job 引用。
        public static JobDef MooGirl_ReadCourierDiary;
        public static JobDef MooGirl_TalkCourier;

        // 奶制食品效果引用。
        public static HediffDef Hediff_MooGirlAgedCheese;
        public static HediffDef Hediff_MooGirlCowCake;
        public static HediffDef Hediff_MooGirlMilkPowder;
        public static HediffDef Hediff_MooGirlMilkTablet;

        // 奶制食品心情引用。
        public static ThoughtDef Consumed_MooGirlPudding;
        public static ThoughtDef Consumed_MooGirlCheese;
        public static ThoughtDef Consumed_MooGirlAgedCheese;
        public static ThoughtDef Consumed_MooGirlRichMilkFlavor;
        public static ThoughtDef Consumed_MooGirlMilkCandy;

        // 奶制食品配方引用。
        public static RecipeDef MooGirl_MakeMilkPudding;
        public static RecipeDef MooGirl_MakeCheese;
        public static RecipeDef MooGirl_MakeAgedCheese;
        public static RecipeDef MooGirl_MakeBaotaSugar;
        public static RecipeDef MooGirl_MakeMilkCandy;
        public static RecipeDef MooGirl_MakeCowCake;
        public static RecipeDef MooGirl_MakeMilkPowder;
        public static RecipeDef MooGirl_MakeMilkTablet;
        public static RecipeDef MooGirl_MakeMilkCreamApple;

        // 快递事件战利品使用既有钥匙 Def，不改 defName 以保护数值与掉落配置。
        public static ThingDef MooGirl_SlaveApperalKey_Medieval;
        public static ThingDef MooGirl_SlaveApperalKey_Industrial;
    }
}
