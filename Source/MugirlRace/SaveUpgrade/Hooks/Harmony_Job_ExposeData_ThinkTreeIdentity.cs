using HarmonyLib;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

[HarmonyPatch(typeof(Job), nameof(Job.ExposeData))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "Legacy job-giver aliases cannot be restricted to jobs read from the main save.")]
internal static class Harmony_Job_ExposeData_ThinkTreeIdentity
{
    internal static void Prefix(Job __instance, ref int ___jobGiverKey, out LegacyThinkJobScope __state)
    {
        if (Scribe.mode == LoadSaveMode.LoadingVars) { UnresolvedThinkTreeKeys.Forget(__instance); }
        __state = LegacyThinkTreeKeys.EnterJob(__instance, SaveUpgradeRuntime.ActiveSession, Scribe.mode, Scribe.loader?.curXmlParent);
        if (Scribe.mode == LoadSaveMode.PostLoadInit && LegacyThinkTreeKeys.RetainMissingTree(__instance, ___jobGiverKey))
        {
            __state = __state with { RetainedKey = ___jobGiverKey };
            ___jobGiverKey = -1;
        }
    }

    [HarmonyPriority(Priority.Last)]
    internal static void Postfix(Job __instance, ref int ___jobGiverKey) => UnresolvedThinkTreeKeys.Save(__instance, ref ___jobGiverKey);

    internal static void Finalizer(ref int ___jobGiverKey, LegacyThinkJobScope __state)
    {
        if (__state.RetainedKey is { } retained) { ___jobGiverKey = retained; }
        LegacyThinkTreeKeys.LeaveJob(__state);
    }
}
