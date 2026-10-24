namespace Mugirl.Features.Grazing;

/// <summary>觅食时植物候选能否通过原版可口度校验的纯规则，供 <see cref="Harmony_FoodUtility_BestFoodSourceOnMap_PlantScan"/> 使用。</summary>
internal static class PlantFoodSearchRules
{
    /// <summary>
    /// 原版 FoodUtility.BestFoodSourceOnMap 开头计算的最低可口度 minPref，规则逐项对应 1.7：
    /// 显式下限优先；非人形或野人不设下限；濒死饥饿接受“仅限绝望时”；紧急饥饿起接受生食；
    /// 其余要求至少是难吃的餐食，带“不介意生食”基因时放宽到生食。
    /// </summary>
    /// <param name="hunger">进食者的饥饿分档；没有饮食需求时为 null（原版此时在这里抛空引用，这里按最宽的生食处理，不影响原版行为）。</param>
    internal static FoodPreferability MinimumPreferability(
        FoodPreferability minPrefOverride,
        bool nonHumanlikeOrWildMan,
        bool desperate,
        HungerCategory? hunger,
        bool dontMindRawFood)
    {
        if (minPrefOverride != FoodPreferability.Undefined)
        {
            return minPrefOverride;
        }

        if (nonHumanlikeOrWildMan)
        {
            return FoodPreferability.NeverForNutrition;
        }

        if (desperate)
        {
            return FoodPreferability.DesperateOnly;
        }

        if (hunger is null || hunger.Value >= HungerCategory.UrgentlyHungry || dontMindRawFood)
        {
            return FoodPreferability.RawBad;
        }

        return FoodPreferability.MealAwful;
    }

    /// <summary>
    /// 原版对候选的第一项校验是可口度不低于 minPref。“食性只含植物”的食物 Def 中最高的可口度仍低于 minPref 时，
    /// 这类候选必然被拒，搜索时不必纳入。
    /// </summary>
    internal static bool PlantsCannotQualify(FoodPreferability minPref, FoodPreferability plantOnlyCeiling) => minPref > plantOnlyCeiling;

    /// <summary>
    /// 原版 FoodSource 组比 FoodSourceNotPlantOrTree 组多出的 Def（可提供营养、食性只含植物或树木）中的最高可口度；
    /// 没有这类 Def 时为 Undefined。分组判定直接调用原版的 ThingRequestGroup.Includes。
    /// </summary>
    internal static FoodPreferability PlantOnlyCeiling(IEnumerable<ThingDef> defs)
    {
        FoodPreferability ceiling = FoodPreferability.Undefined;
        foreach (ThingDef def in defs)
        {
            if (ThingRequestGroup.FoodSource.Includes(def)
                && !ThingRequestGroup.FoodSourceNotPlantOrTree.Includes(def)
                && def.ingestible is { } ingestible
                && ingestible.preferability > ceiling)
            {
                ceiling = ingestible.preferability;
            }
        }

        return ceiling;
    }
}
