namespace Mugirl.Features.Milk;

// 产奶、喂奶与奶制品功能的 Def。
[DefOf]
internal static class MilkDefOf
{
    public static ThingDef Mugirl_Wool = null!;

    public static HediffDef Mugirl_MotherlyNurture = null!;

    public static HediffDef Mugirl_NurtureAfterglow = null!;

    public static TraitDef Mugirl_NurturedByMugirl = null!;

    // 雪牛奶制食品引用。
    public static ThingDef Mugirl_MilkPudding = null!;

    public static ThingDef Mugirl_Cheese = null!;

    public static ThingDef Mugirl_AgedCheese = null!;

    public static ThingDef Mugirl_BaotaSugar = null!;

    public static ThingDef Mugirl_MilkCandy = null!;

    public static ThingDef Mugirl_CowCake = null!;

    public static ThingDef Mugirl_MilkPowder = null!;

    public static ThingDef Mugirl_MilkTablet = null!;

    public static ThingDef Mugirl_MilkCreamApple = null!;

    public static HediffDef Mugirl_Lactation = null!;

    public static HediffDef Mugirl_MilkHealing = null!;

    public static ThoughtDef Mugirl_ConsumedMilk = null!;

    public static ThoughtDef Mugirl_DrankMilk = null!;

    public static ThoughtDef Mugirl_FedMilk = null!;

    public static ThingDef Mugirl_Filth_MilkStain = null!;

    public static SoundDef Mugirl_Milking_Sound = null!;

    public static JobDef Mugirl_DrinkMilk = null!;

    public static JobDef Mugirl_ChildDrinkMilk = null!;

    public static JobDef Mugirl_FeedMilk = null!;

    public static FleckDef FoamSpray = null!;

    public static ThingDef Mugirl_Milk = null!;

    public static JobDef Mugirl_GatherMilk = null!;

    public static JobDef Job_MugirlMilkingTargetWait = null!;

    public static JobDef Mugirl_MilkInteractionWait = null!;

    public static AnimationDef Mugirl_MilkingSelfAnimation = null!;

    public static AnimationDef Mugirl_MilkingTargetAnimation = null!;

    public static AnimationDef Mugirl_MilkingHelperAnimation = null!;

    public static AnimationDef Mugirl_MilkInteractionSourceAnimation = null!;

    public static AnimationDef Mugirl_MilkInteractionRecipientAnimation = null!;

    public static PawnRenderNodeTagDef Root = null!;

    static MilkDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(MilkDefOf));
}
