using HarmonyLib;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

[HarmonyPatch(typeof(ThinkTreeKeyAssigner), nameof(ThinkTreeKeyAssigner.AssignKeys))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "Renamed ThinkNode classes or tree definitions cannot recover old Job giver identities.")]
internal static class Harmony_ThinkTreeKeyAssigner_AssignKeys
{
    [HarmonyPriority(Priority.Last)]
    internal static void Prefix(ThinkNode rootNode, int startHash, HashSet<int> ___assignedKeys)
        => LegacyThinkTreeKeys.Capture(rootNode, startHash, single: false, ___assignedKeys.Count);

    [HarmonyPriority(Priority.Last)]
    internal static void Postfix(ThinkNode rootNode) => LegacyThinkTreeKeys.VerifyCapture(rootNode, single: false);
}
