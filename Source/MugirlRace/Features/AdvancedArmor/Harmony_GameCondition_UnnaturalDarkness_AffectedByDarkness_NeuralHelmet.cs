using HarmonyLib;

namespace Mugirl.Features.AdvancedArmor;

// 神经增幅头盔：佩戴者不受异常黑暗影响。
[HarmonyPatch(typeof(GameCondition_UnnaturalDarkness), nameof(GameCondition_UnnaturalDarkness.AffectedByDarkness))]
[MugirlPatch(PatchModule.AdvancedArmor, PatchRisk.Medium, "The neural amplifier helmet no longer protects against unnatural darkness.")]
internal static class Harmony_GameCondition_UnnaturalDarkness_AffectedByDarkness_NeuralHelmet
{
    private static void Postfix(Pawn? pawn, ref bool __result)
    {
        if (__result && AdvancedArmorUtility.WearsNeuralHelmet(pawn))
        {
            __result = false;
        }
    }
}
