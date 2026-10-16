using HarmonyLib;

namespace Mugirl.Features.Restraints;

[HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.WouldReplaceLockedApparel))]
[MugirlPatch(PatchModule.Restraints, PatchRisk.High, "Force-wear checks ignore locked slave apparel that would be replaced by the new apparel.")]
public static class Harmony_Pawn_ApparelTracker_WouldReplaceLockedApparel
{
    public static void Postfix(Pawn_ApparelTracker __instance, Apparel newApparel, ref bool __result)
    {
        if (!__result && RestraintWearGuard.WouldReplaceLockedSlaveApparel(__instance.pawn, newApparel))
        {
            __result = true;
        }
    }
}
