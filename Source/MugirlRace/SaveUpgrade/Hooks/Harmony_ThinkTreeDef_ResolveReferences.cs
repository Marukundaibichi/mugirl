using HarmonyLib;

namespace Mugirl.SaveUpgrade;

[HarmonyPatch(typeof(ThinkTreeDef), nameof(ThinkTreeDef.ResolveReferences))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "The legacy random seed of a renamed ThinkTree cannot be identified.")]
internal static class Harmony_ThinkTreeDef_ResolveReferences
{
    [HarmonyPriority(Priority.First)]
    internal static void Prefix(ThinkTreeDef __instance, out ThinkTreeDef? __state) => __state = LegacyThinkTreeKeys.EnterTree(__instance);

    internal static void Finalizer(ThinkTreeDef? __state) => LegacyThinkTreeKeys.LeaveTree(__state);
}
