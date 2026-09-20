using HarmonyLib;
using Mugirl.Features.Appearance;
using RimWorld;
using Verse;

namespace Mugirl
{
    // Vanilla 只在一次 Lovin 完成后调用此方法；Postfix 不改写间隔和原有结果。
    [HarmonyPatch(typeof(JobDriver_Lovin), "GenerateRandomMinTicksToNextLovin")]
    internal static class Harmony_LovinBodyAccessory
    {
        [HarmonyPostfix]
        private static void Postfix(Pawn pawn)
        {
            pawn?.TryGetComp<CompMugirlBodyAccessory>()?.TryApplyLovinMark();
        }
    }
}
