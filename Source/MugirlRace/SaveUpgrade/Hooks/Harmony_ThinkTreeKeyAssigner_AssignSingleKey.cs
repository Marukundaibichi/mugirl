using HarmonyLib;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

[HarmonyPatch(typeof(ThinkTreeKeyAssigner), nameof(ThinkTreeKeyAssigner.AssignSingleKey))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "Copied ThinkNodes cannot retain the old collision allocation order.")]
internal static class Harmony_ThinkTreeKeyAssigner_AssignSingleKey
{
    [HarmonyPriority(Priority.Last)]
    internal static void Prefix(ThinkNode node, int startHash, HashSet<int> ___assignedKeys)
        => LegacyThinkTreeKeys.Capture(node, startHash, single: true, ___assignedKeys.Count);

    [HarmonyPriority(Priority.Last)]
    internal static void Postfix(ThinkNode node) => LegacyThinkTreeKeys.VerifyCapture(node, single: true);
}
