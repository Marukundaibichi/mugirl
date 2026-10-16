using HarmonyLib;

namespace Mugirl.Features.Social;

[HarmonyPatch(typeof(HediffComp_GiveRandomSituationalThought), nameof(HediffComp_GiveRandomSituationalThought.CompPostMake))]
[MugirlPatch(PatchModule.Social, PatchRisk.Low, "雪牛娘新生成的怀孕心情恢复原版随机结果，其他种族不受影响。")]
internal static class Harmony_HediffComp_GiveRandomSituationalThought_CompPostMake_PregnancyMood
{
    private static void Postfix(HediffComp_GiveRandomSituationalThought __instance)
    {
        PregnancyMoodUtility.EnsurePositiveThought(__instance);
    }
}
