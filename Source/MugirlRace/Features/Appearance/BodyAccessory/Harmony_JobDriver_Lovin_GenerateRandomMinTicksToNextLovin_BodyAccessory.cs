using HarmonyLib;

namespace Mugirl.Features.Appearance;

// 原版只在一次 Lovin 完成后调用此私有方法（MakeNewToils 的结束回调）；Postfix 不改写间隔和原有结果。
[HarmonyPatch(typeof(JobDriver_Lovin), "GenerateRandomMinTicksToNextLovin", typeof(Pawn))]
[MugirlPatch(PatchModule.Appearance, PatchRisk.Low, "Lovin completes normally, but no temporary bite or handprint body mark is added.")]
internal static class Harmony_JobDriver_Lovin_GenerateRandomMinTicksToNextLovin_BodyAccessory
{
    [HarmonyPostfix]
    private static void Postfix(Pawn? pawn)
    {
        pawn?.TryGetComp<CompBodyAccessory>()?.TryApplyLovinMark();
    }
}
