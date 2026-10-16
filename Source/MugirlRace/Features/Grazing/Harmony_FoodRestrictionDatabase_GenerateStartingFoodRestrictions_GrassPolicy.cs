using HarmonyLib;

namespace Mugirl.Features.Grazing;

// 新开游戏：跟随原版初始预设一起生成“草”。
[HarmonyPatch(typeof(FoodRestrictionDatabase), "GenerateStartingFoodRestrictions")]
[MugirlPatch(PatchModule.Grazing, PatchRisk.Low, "New games do not receive the grass food policy preset for Mugirl grazing.")]
internal static class Harmony_FoodRestrictionDatabase_GenerateStartingFoodRestrictions_GrassPolicy
{
    internal static void Postfix(FoodRestrictionDatabase __instance)
    {
        GrassFoodPolicyUtility.EnsureGrassPolicy(__instance);
    }
}
