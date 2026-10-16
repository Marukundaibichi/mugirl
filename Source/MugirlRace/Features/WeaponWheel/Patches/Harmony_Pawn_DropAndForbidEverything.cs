using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

[HarmonyPatch(typeof(Pawn), nameof(Pawn.DropAndForbidEverything))]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.High, "Reserve weapons are not dropped or destroyed with ordinary pawn gear.")]
public static class Harmony_Pawn_DropAndForbidEverything
{
    public static void Prefix(Pawn __instance, bool keepInventoryAndEquipmentIfInBed)
    {
        WeaponWheelLookup.CompFor(__instance)?.HandleDropAndForbidEverything(keepInventoryAndEquipmentIfInBed);
    }
}
