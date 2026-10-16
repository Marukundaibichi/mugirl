namespace Mugirl.Features.Grazing;

// 食物方案预设“草”：仅允许官方内容里不可种植（plant.Sowable 为 false）的可食用植物。
// 分配给雪牛娘后，她只吃野草、灌木等野生植物，不碰殖民地食物与地里种的农作物。
// 饥饿急迫起原版食物搜索接受 RawBad 可口度的植物，配合本模组的吃草 Job 补丁即可自动觅食，
// 因此这里只需要生成方案本体，不新增任何进食逻辑。
internal static class GrassFoodPolicyUtility
{
    // StaticCacheLifecycle: 进程级只读标签表，不持有游戏对象。
    // 方案在食物方案列表中的去重标签；同时匹配中英文，玩家切换语言后旧档也不会重复生成。
    // 玩家手动重命名方案名会导致下次读档时重复生成一个同名预设，属无害行为。
    private static readonly string[] GrassPolicyLabels = { "草", "Grass" };

    internal static void EnsureGrassPolicy(FoodRestrictionDatabase? database)
    {
        if (database is null)
        {
            return;
        }

        string label = "Mugirl.FoodPolicy.Grass.Label".Translate();
        foreach (FoodPolicy policy in database.AllFoodRestrictions)
        {
            if (policy is not null && (policy.label == label || IsKnownGrassLabel(policy.label)))
            {
                return;
            }
        }

        FoodPolicy grassPolicy = database.MakeNewFoodRestriction();
        grassPolicy.label = label;
        grassPolicy.filter.SetDisallowAll();
        foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
        {
            if (IsWildVanillaPlantFood(def))
            {
                grassPolicy.filter.SetAllow(def, true);
            }
        }
    }

    private static bool IsKnownGrassLabel(string? label)
    {
        // 标签是玩家可见文本，string == 为序数比较，不受 1.7 ICU 文化比较影响。
        for (int i = 0; i < GrassPolicyLabels.Length; i++)
        {
            if (label == GrassPolicyLabels[i])
            {
                return true;
            }
        }

        return false;
    }

    // 原版（Core 或官方 DLC）中带 ingestible 的植物 ThingDef，且不可种植（Sowable 为 false），
    // 即“地里种的农作物”之外的可食用野生植物；判定口径与 GrazeUtility.IsPlantFoodDef 一致。
    private static bool IsWildVanillaPlantFood(ThingDef? def)
    {
        if (def?.ingestible is null || def.plant is null)
        {
            return false;
        }

        if (!typeof(Plant).IsAssignableFrom(def.thingClass))
        {
            return false;
        }

        ModContentPack? pack = def.modContentPack;
        if (pack is null || !(pack.IsCoreMod || pack.IsOfficialMod))
        {
            return false;
        }

        return !def.plant.Sowable;
    }
}
