using HarmonyLib;

namespace Mugirl.Features.Grazing;

// 读取旧档：方案列表加载完毕后补生成“草”，已存在则跳过。
[HarmonyPatch(typeof(FoodRestrictionDatabase), nameof(FoodRestrictionDatabase.ExposeData))]
[MugirlPatch(PatchModule.Grazing, PatchRisk.Low, "Existing saves do not receive the grass food policy preset for Mugirl grazing.")]
internal static class Harmony_FoodRestrictionDatabase_ExposeData_GrassPolicy
{
    internal static void Postfix(FoodRestrictionDatabase __instance)
    {
        if (Scribe.mode != LoadSaveMode.PostLoadInit)
        {
            return;
        }

        GrassFoodPolicyUtility.EnsureGrassPolicy(__instance);
    }
}
