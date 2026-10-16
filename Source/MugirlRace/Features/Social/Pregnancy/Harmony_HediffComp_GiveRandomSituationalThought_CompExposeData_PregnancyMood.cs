using HarmonyLib;

namespace Mugirl.Features.Social;

[HarmonyPatch(typeof(HediffComp_GiveRandomSituationalThought), nameof(HediffComp_GiveRandomSituationalThought.CompExposeData))]
[MugirlPatch(PatchModule.Social, PatchRisk.Low, "旧存档中雪牛娘已有的负面怀孕心情保留到自然消失，其他存档数据不受影响。")]
internal static class Harmony_HediffComp_GiveRandomSituationalThought_CompExposeData_PregnancyMood
{
    private static void Postfix(HediffComp_GiveRandomSituationalThought __instance)
    {
        // 读档完成后再转换旧负面状态；保留既有正面状态及剩余持续时间。
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            PregnancyMoodUtility.EnsurePositiveThought(__instance);
        }
    }
}
