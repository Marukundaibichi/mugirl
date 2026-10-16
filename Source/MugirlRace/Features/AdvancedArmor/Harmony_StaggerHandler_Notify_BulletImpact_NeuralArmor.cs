using HarmonyLib;

namespace Mugirl.Features.AdvancedArmor;

// 神经接入战斗护甲：穿戴者不受子弹命中造成的硬直。
[HarmonyPatch(typeof(StaggerHandler), nameof(StaggerHandler.Notify_BulletImpact))]
[MugirlPatch(PatchModule.AdvancedArmor, PatchRisk.Medium, "Neural combat armor wearers receive normal bullet stagger.")]
internal static class Harmony_StaggerHandler_Notify_BulletImpact_NeuralArmor
{
    private static bool Prefix(StaggerHandler? __instance)
    {
        return !AdvancedArmorUtility.WearsNeuralArmor(__instance?.parent);
    }
}
