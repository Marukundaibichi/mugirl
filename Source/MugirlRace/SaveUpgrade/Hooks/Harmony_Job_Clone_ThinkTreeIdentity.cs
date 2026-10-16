using HarmonyLib;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

[HarmonyPatch(typeof(Job), nameof(Job.Clone))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "Cloning a Job could lose its unresolved saved source key.")]
internal static class Harmony_Job_Clone_ThinkTreeIdentity
{
    internal static void Postfix(Job __instance, Job __result) => UnresolvedThinkTreeKeys.Copied(__instance, __result);
}
