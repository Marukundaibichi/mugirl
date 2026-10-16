using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

[HarmonyPatch(typeof(MassUtility), nameof(MassUtility.GearMass))]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.Medium, "Reserve weapon mass is omitted from gear mass.")]
public static class Harmony_MassUtility_GearMass
{
    public static void Postfix(Pawn p, ref float __result)
    {
        CompWeaponWheel? comp = WeaponWheelLookup.CompFor(p);
        if (comp != null)
        {
            __result += comp.ReserveWeaponMass;
        }
    }
}
