using HarmonyLib;

namespace Mugirl.Features.AdvancedArmor;

// 神经增幅头盔：屏蔽心灵波动带来的心情影响。目标是受保护的 override，只能按名称指定。
[HarmonyPatch(typeof(ThoughtWorker_PsychicDrone), "CurrentStateInternal")]
[MugirlPatch(PatchModule.AdvancedArmor, PatchRisk.Medium, "The neural amplifier helmet no longer suppresses psychic-drone thoughts.")]
internal static class Harmony_ThoughtWorker_PsychicDrone_CurrentStateInternal_NeuralHelmet
{
    private static bool Prefix(Pawn? p, ref ThoughtState __result)
    {
        if (!AdvancedArmorUtility.WearsNeuralHelmet(p))
        {
            return true;
        }

        __result = ThoughtState.Inactive;
        return false;
    }
}
