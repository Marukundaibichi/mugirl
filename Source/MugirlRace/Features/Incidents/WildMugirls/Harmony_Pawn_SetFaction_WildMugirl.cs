using HarmonyLib;

namespace Mugirl.Features.Incidents;

[HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
[MugirlPatch(PatchModule.Incidents, PatchRisk.Medium, "Transient wild-slave event state cleanup after joining the player is skipped.")]
internal static class Harmony_Pawn_SetFaction_WildMugirl
{
    public static void Postfix(Pawn __instance, Faction newFaction)
    {
        if (PlayerFactionUtility.IsPlayerFaction(newFaction) && WildMugirlUtility.IsMugirlPawn(__instance))
        {
            WildMugirlUtility.CleanupAfterJoiningPlayer(__instance);
        }
    }
}
