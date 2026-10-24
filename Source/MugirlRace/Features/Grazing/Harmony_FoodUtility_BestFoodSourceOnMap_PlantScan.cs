using HarmonyLib;

namespace Mugirl.Features.Grazing;

// 雪牛娘的食性含植物，原版人形觅食因此把全图植物都列为候选，并在可口度校验之前逐株计算食物评分：
// 植物多的地图上一次觅食要十几到几十毫秒，找不到能吃的饭时每次思考都会重扫一遍。
// 未到紧急饥饿时原版要求至少是难吃的餐食，而植物最高只是生食，植物候选必然被原版校验拒绝；
// 这时改用原版不含植物的候选组，选出的食物与原版相同。紧急饥饿、濒死饥饿与野生状态下植物仍是候选，搜索不变。
[HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.BestFoodSourceOnMap))]
[MugirlPatch(PatchModule.Grazing, PatchRisk.Low, "Mugirls looking for food score every plant on the map even when no plant is acceptable, which stalls the game on plant-rich maps; the food they pick is unchanged.")]
public static class Harmony_FoodUtility_BestFoodSourceOnMap_PlantScan
{
    // StaticCacheLifecycle: 进程级；只由已加载的 ThingDef 推导，游戏运行期间 Def 不变，第一次觅食时计算。
    private static FoodPreferability? plantOnlyCeiling;

    // 排在所有前缀之后，判断依据是其他补丁改过之后的参数。
    [HarmonyPriority(Priority.Last)]
    public static void Prefix(Pawn eater, bool desperate, ref bool allowPlant, FoodPreferability minPrefOverride)
    {
        if (!allowPlant || !MugirlIdentity.IsMugirlPawn(eater))
        {
            return;
        }

        FoodPreferability minPref = PlantFoodSearchRules.MinimumPreferability(
            minPrefOverride,
            eater.NonHumanlikeOrWildMan(),
            desperate,
            eater.needs?.food?.CurCategory,
            eater.genes is { DontMindRawFood: true });
        plantOnlyCeiling ??= PlantFoodSearchRules.PlantOnlyCeiling(DefDatabase<ThingDef>.AllDefsListForReading);
        if (PlantFoodSearchRules.PlantsCannotQualify(minPref, plantOnlyCeiling.Value))
        {
            allowPlant = false;
        }
    }
}
