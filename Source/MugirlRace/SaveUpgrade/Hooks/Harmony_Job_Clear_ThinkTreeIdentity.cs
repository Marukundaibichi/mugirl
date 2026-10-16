using HarmonyLib;
using Verse.AI;

namespace Mugirl.SaveUpgrade;

[HarmonyPatch(typeof(Job), nameof(Job.Clear))]
[MugirlPatch(PatchModule.SaveUpgrade, PatchRisk.High, "An unresolved source key could survive Job pool reuse.")]
internal static class Harmony_Job_Clear_ThinkTreeIdentity
{
    internal static void Postfix(Job __instance) => UnresolvedThinkTreeKeys.Forget(__instance);
}
