using HarmonyLib;

namespace Mugirl.SaveUpgrade;

[HarmonyPatch(typeof(ThinkTreeKeyAssigner), nameof(ThinkTreeKeyAssigner.Reset))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "Legacy ThinkTree identities may retain a previous Def key space.")]
internal static class Harmony_ThinkTreeKeyAssigner_Reset
{
    [HarmonyPriority(Priority.Last)]
    internal static void Postfix() => LegacyThinkTreeKeys.Reset();
}
