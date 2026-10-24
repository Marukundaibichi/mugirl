using Mugirl.Features.Grazing;
using RimWorld;

namespace Mugirl.Tests.Grazing;

/// <summary>觅食时跳过植物候选的纯规则：最低可口度与原版逐项一致，只在植物必然被拒时跳过。</summary>
public sealed class PlantFoodSearchRulesTests
{
    [Fact]
    public void MinimumPreferability_MatchesVanilla17ForEveryInput()
    {
        foreach (FoodPreferability minPrefOverride in Enum.GetValues<FoodPreferability>())
        {
            foreach (bool nonHumanlikeOrWildMan in new[] { false, true })
            {
                foreach (bool desperate in new[] { false, true })
                {
                    foreach (HungerCategory hunger in Enum.GetValues<HungerCategory>())
                    {
                        foreach (bool dontMindRawFood in new[] { false, true })
                        {
                            FoodPreferability expected = Vanilla(minPrefOverride, nonHumanlikeOrWildMan, desperate, hunger, dontMindRawFood);
                            FoodPreferability actual = PlantFoodSearchRules.MinimumPreferability(minPrefOverride, nonHumanlikeOrWildMan, desperate, hunger, dontMindRawFood);
                            Assert.True(expected == actual,
                                $"override {minPrefOverride}, nonHumanlikeOrWildMan {nonHumanlikeOrWildMan}, desperate {desperate}, hunger {hunger}, dontMindRawFood {dontMindRawFood}: expected {expected}, got {actual}");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void MinimumPreferability_WithoutFoodNeedIsTheWidestHumanlikeFloor()
    {
        // 原版在没有饮食需求时会抛空引用；这里取不跳过植物的最宽下限。
        Assert.Equal(FoodPreferability.RawBad, PlantFoodSearchRules.MinimumPreferability(FoodPreferability.Undefined, false, false, null, false));
        Assert.Equal(FoodPreferability.DesperateOnly, PlantFoodSearchRules.MinimumPreferability(FoodPreferability.Undefined, false, true, null, false));
    }

    [Theory]
    [InlineData(FoodPreferability.MealAwful, FoodPreferability.RawBad, true)]
    [InlineData(FoodPreferability.RawTasty, FoodPreferability.RawBad, true)]
    [InlineData(FoodPreferability.RawBad, FoodPreferability.RawBad, false)]
    [InlineData(FoodPreferability.DesperateOnly, FoodPreferability.RawBad, false)]
    [InlineData(FoodPreferability.NeverForNutrition, FoodPreferability.RawBad, false)]
    [InlineData(FoodPreferability.MealAwful, FoodPreferability.MealAwful, false)]
    [InlineData(FoodPreferability.NeverForNutrition, FoodPreferability.Undefined, true)]
    public void PlantsCannotQualify_OnlyWhenTheFloorIsAboveEveryPlantOnlyFood(FoodPreferability minPref, FoodPreferability ceiling, bool expected)
        => Assert.Equal(expected, PlantFoodSearchRules.PlantsCannotQualify(minPref, ceiling));

    // 原版 1.7 FoodUtility.BestFoodSourceOnMap 开头计算 minPref 的代码，逐句照抄作参照。
    private static FoodPreferability Vanilla(FoodPreferability minPrefOverride, bool nonHumanlikeOrWildMan, bool desperate, HungerCategory hunger, bool dontMindRawFood)
    {
        FoodPreferability minPref;
        if (minPrefOverride == FoodPreferability.Undefined)
        {
            if (nonHumanlikeOrWildMan)
            {
                minPref = FoodPreferability.NeverForNutrition;
            }
            else if (desperate)
            {
                minPref = FoodPreferability.DesperateOnly;
            }
            else
            {
                minPref = ((int)hunger >= 2) ? FoodPreferability.RawBad : FoodPreferability.MealAwful;
                if (minPref == FoodPreferability.MealAwful && dontMindRawFood)
                {
                    minPref = FoodPreferability.RawBad;
                }
            }
        }
        else
        {
            minPref = minPrefOverride;
        }

        return minPref;
    }
}
