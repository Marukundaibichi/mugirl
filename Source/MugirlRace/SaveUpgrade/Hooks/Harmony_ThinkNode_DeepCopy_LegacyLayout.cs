using HarmonyLib;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

[HarmonyPatch(typeof(ThinkNode), nameof(ThinkNode.DeepCopy))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "Historical ThinkTree keys cannot exclude duties introduced after the published save layout.")]
internal static class Harmony_ThinkNode_DeepCopy_LegacyLayout
{
    [HarmonyPriority(Priority.First)]
    internal static void Prefix(ThinkNode __instance, out LegacyThinkCopyScope __state)
        => __state = LegacyThinkLayouts.EnterCopy(__instance);

    [HarmonyPriority(Priority.Last)]
    internal static void Postfix(ThinkNode __result, LegacyThinkCopyScope __state)
        => LegacyThinkLayouts.Copied(__result, __state);

    internal static void Finalizer(LegacyThinkCopyScope __state) => LegacyThinkLayouts.LeaveCopy(__state);
}
