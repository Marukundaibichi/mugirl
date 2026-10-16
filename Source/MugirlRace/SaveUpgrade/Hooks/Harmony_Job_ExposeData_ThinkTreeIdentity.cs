using HarmonyLib;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

[HarmonyPatch(typeof(Job), nameof(Job.ExposeData))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "Legacy job-giver aliases cannot be restricted to jobs read from the main save.")]
internal static class Harmony_Job_ExposeData_ThinkTreeIdentity
{
    internal static void Prefix(Job __instance, out LegacyThinkJobScope __state) => __state = LegacyThinkTreeKeys.EnterJob(
        __instance, SaveUpgradeRuntime.ActiveSession, Scribe.mode, Scribe.loader?.curXmlParent);

    internal static void Finalizer(LegacyThinkJobScope __state) => LegacyThinkTreeKeys.LeaveJob(__state);
}
