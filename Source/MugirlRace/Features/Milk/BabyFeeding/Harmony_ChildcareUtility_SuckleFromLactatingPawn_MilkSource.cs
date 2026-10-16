using HarmonyLib;

namespace Mugirl.Features.Milk;

// 雪牛娘哺乳者由奶量结算吸奶并跳过原版（原版要求 Lactating Hediff）；奶量耗尽且带原版哺乳 Hediff 时交还原版。
// Lactation Expansion 启用时由 Compatibility 模块中优先级更高的同目标补丁先处理，并阻止原方法与本补丁重复消耗。
[HarmonyPatch(typeof(ChildcareUtility), nameof(ChildcareUtility.SuckleFromLactatingPawn))]
[MugirlPatch(PatchModule.Milk, PatchRisk.High, "Babies use the base lactation path and will not consume Mugirl milk fullness.")]
public static class Harmony_ChildcareUtility_SuckleFromLactatingPawn_MilkSource
{
    [HarmonyPriority(Priority.Normal)]
    public static bool Prefix(Pawn baby, Pawn feeder, int delta, ref bool __result)
    {
        if (!BabyFeedingUtility.HasMooMilkSource(feeder, out CompMilkProduction? milkComp))
        {
            return true;
        }

        if (BabyFeedingUtility.AvailableMilkNutrition(milkComp, out float nutritionPerFullness) <= 0f)
        {
            if (BabyFeedingUtility.HasVanillaLactation(feeder))
            {
                return true;
            }

            __result = false;
            return false;
        }

        __result = BabyFeedingUtility.SuckleFromMugirl(baby, feeder, milkComp, delta, nutritionPerFullness);
        return false;
    }
}
