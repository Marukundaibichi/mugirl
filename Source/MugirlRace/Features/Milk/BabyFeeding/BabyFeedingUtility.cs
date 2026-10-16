using System.Diagnostics.CodeAnalysis;
using RimWorld.Planet;

namespace Mugirl.Features.Milk;

// Biotech 婴儿喂养：让雪牛娘的奶量（而不是原版 Lactating Hediff）满足哺乳判定并被婴儿消耗。
// 公开给 Compatibility 模块的 Lactation Expansion 兼容补丁使用。
public static class BabyFeedingUtility
{
    private const float MinBreastfeedNutrition = 0.031f;

    // 雪牛奶 Def 缺失时每 1.0 饱满度对应的营养。
    private const float FallbackNutritionPerFullness = 10f;

    public static bool CanMugirlBreastfeed([NotNullWhen(true)] Pawn? mom, out ChildcareUtility.BreastfeedFailReason? reason)
    {
        reason = GetBreastfeedFailReason(mom);
        return reason == null && mom != null;
    }

    public static bool CanMugirlBreastfeedNow([NotNullWhen(true)] Pawn? mom, out ChildcareUtility.BreastfeedFailReason? reason)
    {
        if (!CanMugirlBreastfeed(mom, out reason))
        {
            return false;
        }

        if (!HasMooMilkSource(mom, out CompMilkProduction? milkComp) || AvailableMilkNutrition(milkComp) < MinBreastfeedNutrition)
        {
            reason = ChildcareUtility.BreastfeedFailReason.MomNotEnoughMilk;
            return false;
        }

        if (mom.InMentalState && !mom.Downed)
        {
            reason = ChildcareUtility.BreastfeedFailReason.MomInMentalState;
            return false;
        }

        return true;
    }

    public static bool HasMooMilkSource([NotNullWhen(true)] Pawn? pawn, [NotNullWhen(true)] out CompMilkProduction? milkComp)
    {
        milkComp = pawn?.TryGetComp<CompMilkProduction>();
        return pawn != null && milkComp != null && milkComp.Active;
    }

    public static bool HasVanillaLactation(Pawn? pawn)
    {
        return pawn?.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Lactating) != null;
    }

    public static float AvailableMilkNutrition(CompMilkProduction? milkComp)
    {
        return AvailableMilkNutrition(milkComp, out _);
    }

    internal static float AvailableMilkNutrition(CompMilkProduction? milkComp, out float nutritionPerFullness)
    {
        nutritionPerFullness = 0f;
        if (milkComp == null)
        {
            return 0f;
        }

        nutritionPerFullness = NutritionPerFullness(milkComp);
        return MilkSuckleMath.AvailableNutrition(milkComp.Fullness, nutritionPerFullness);
    }

    public static bool SuckleFromMugirl(Pawn? baby, Pawn? feeder, CompMilkProduction? milkComp, int delta)
    {
        return SuckleFromMugirl(baby, feeder, milkComp, delta, null);
    }

    // 营养倍率只在本次喂食调用内传递，不跨 tick 缓存，保留其他 mod 动态修改属性的能力。
    internal static bool SuckleFromMugirl(Pawn? baby, Pawn? feeder, CompMilkProduction? milkComp, int delta, float? nutritionPerFullness)
    {
        Need_Food? food = baby?.needs?.food;
        if (baby == null || food == null || feeder == null || milkComp == null || delta <= 0 || food.MaxLevel <= 0f)
        {
            return false;
        }

        float nutritionWanted = UnityMathf.Max(0f, food.NutritionWanted);
        float desiredNutrition = MilkSuckleMath.DesiredNutrition(food.MaxLevel, nutritionWanted, delta);
        float consumedNutrition = ConsumeMilkNutrition(milkComp, desiredNutrition, nutritionPerFullness);
        if (consumedNutrition <= 0f)
        {
            return false;
        }

        food.CurLevel = UnityMathf.Min(food.MaxLevel, food.CurLevel + consumedNutrition);
        NurtureUtility.AddBabyNurtureProgress(baby, feeder, delta);

        Caravan? caravan = baby.GetCaravan();
        if (caravan != null && feeder.GetCaravan() == caravan && feeder.mindState != null)
        {
            feeder.mindState.BreastfeedCaravan(baby, UnityMathf.Clamp01(consumedNutrition / food.MaxLevel));
        }

        if (feeder.ideo != null)
        {
            baby.ideo?.IncreaseIdeoExposureIfBabyTick(feeder.Ideo);
        }

        return MilkSuckleMath.ShouldContinue(consumedNutrition, nutritionWanted, desiredNutrition);
    }

    private static float ConsumeMilkNutrition(CompMilkProduction milkComp, float desiredNutrition, float? knownNutritionPerFullness)
    {
        if (desiredNutrition <= 0f)
        {
            return 0f;
        }

        float nutritionPerFullness = knownNutritionPerFullness ?? NutritionPerFullness(milkComp);
        if (nutritionPerFullness <= 0f)
        {
            return 0f;
        }

        float availableNutrition = MilkSuckleMath.AvailableNutrition(milkComp.Fullness, nutritionPerFullness);
        float consumedNutrition = UnityMathf.Min(desiredNutrition, availableNutrition);
        if (consumedNutrition <= 0f)
        {
            return 0f;
        }

        milkComp.ConsumePercentage(consumedNutrition / nutritionPerFullness);
        return consumedNutrition;
    }

    // 按原版 CanBreastfeed 的检查顺序给出第一个失败原因；可以哺乳时为 null。
    private static ChildcareUtility.BreastfeedFailReason? GetBreastfeedFailReason(Pawn? mom)
    {
        if (!ModsConfig.BiotechActive)
        {
            return ChildcareUtility.BreastfeedFailReason.NoBiotechMod;
        }

        if (mom == null)
        {
            return ChildcareUtility.BreastfeedFailReason.MomNull;
        }

        if (mom.Dead)
        {
            return ChildcareUtility.BreastfeedFailReason.MomDead;
        }

        if (mom.RaceProps == null || !mom.RaceProps.Humanlike)
        {
            return ChildcareUtility.BreastfeedFailReason.MomNotHumanLike;
        }

        return HasMooMilkSource(mom, out _) ? null : ChildcareUtility.BreastfeedFailReason.MomNotLactating;
    }

    private static float NutritionPerFullness(CompMilkProduction milkComp)
    {
        ThingDef? milkDef = milkComp.Props?.milkDef;
        if (milkDef == null)
        {
            return FallbackNutritionPerFullness;
        }

        return UnityMathf.Max(0.001f, milkDef.GetStatValueAbstract(StatDefOf.Nutrition) * 100f);
    }
}
