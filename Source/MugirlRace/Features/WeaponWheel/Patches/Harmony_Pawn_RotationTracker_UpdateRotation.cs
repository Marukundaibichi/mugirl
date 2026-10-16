using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

[HarmonyPatch(typeof(Pawn_RotationTracker), nameof(Pawn_RotationTracker.UpdateRotation))]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.High, "Active side-facing wheel fire may briefly fall back to the drafted pawn's south-facing idle rotation.")]
public static class Harmony_Pawn_RotationTracker_UpdateRotation
{
    public static void Postfix(Pawn ___pawn)
    {
        WeaponWheelLookup.CompFor(___pawn)?.MaintainCombatFacing();
    }
}
