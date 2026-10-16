using HarmonyLib;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

[HarmonyPatch(typeof(ThinkTreeDef), nameof(ThinkTreeDef.TryGetThinkNodeWithSaveKey))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "Jobs loaded from older Mugirl versions lose their issuing ThinkNode after internal renames.")]
internal static class Harmony_ThinkTreeDef_TryGetThinkNodeWithSaveKey
{
    [HarmonyPriority(Priority.First)]
    internal static bool Prefix(ThinkTreeDef __instance, ref int key, ref ThinkNode? outNode, ref bool __result, out LegacyThinkLookup? __state)
    {
        if (LegacyThinkTreeKeys.PrepareLookup(__instance, ref key, out __state))
        {
            return true;
        }

        outNode = null;
        __result = false;
        return false;
    }

    [HarmonyPriority(Priority.Last)]
    internal static void Postfix(ThinkTreeDef __instance, int key, ref ThinkNode? outNode, ref bool __result, LegacyThinkLookup? __state)
    {
        LegacyThinkTreeKeys.CompleteLookup(__instance, __state, ref outNode, ref __result);
        LegacyThinkTreeKeys.RememberNativeFailure(__instance, key, outNode, __result, __state);
    }

    [HarmonyPriority(Priority.Last)]
    internal static Exception? Finalizer(Exception? __exception, LegacyThinkLookup? __state)
    {
        LegacyThinkTreeKeys.LookupFailed(__state, __exception);
        return __exception;
    }
}
