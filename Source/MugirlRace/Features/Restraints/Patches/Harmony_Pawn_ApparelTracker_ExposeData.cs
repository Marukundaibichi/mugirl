using HarmonyLib;

namespace Mugirl.Features.Restraints;

[HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.ExposeData))]
[MugirlPatch(PatchModule.Restraints, PatchRisk.High, "Older saves can leave worn slave apparel missing from the base locked-apparel list.")]
public static class Harmony_Pawn_ApparelTracker_ExposeData
{
    public static void Postfix(Pawn_ApparelTracker __instance)
    {
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            __instance?.pawn.EnsureWornRestraintLocks();
        }
    }
}
