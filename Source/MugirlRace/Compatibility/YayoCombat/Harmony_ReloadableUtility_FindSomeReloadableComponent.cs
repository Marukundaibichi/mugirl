using HarmonyLib;
using Mugirl.Features.WeaponWheel;
using RimWorld.Utility;

namespace Mugirl.Compatibility.YayoCombat;

[HarmonyPatch(typeof(ReloadableUtility), nameof(ReloadableUtility.FindSomeReloadableComponent))]
[MugirlPatch(PatchModule.YayoCombat, PatchRisk.Medium, "Yayo's Combat reload discovery omits empty reserve weapons.")]
internal static class Harmony_ReloadableUtility_FindSomeReloadableComponent
{
    public static bool Prepare() => YayoCombatCompatibility.Active;

    [HarmonyAfter(ModCompat.YayoCombatHarmonyId)]
    public static void Postfix(Pawn pawn, bool allowForcedReload, ref IReloadableComp? __result)
    {
        if (__result is not null || !YayoCombatCompatibility.AmmoEnabled)
        {
            return;
        }

        ThingOwner<ThingWithComps>? reserveWeapons = WeaponWheelLookup.CompFor(pawn)?.ReserveWeapons;
        if (reserveWeapons is null)
        {
            return;
        }

        for (int i = 0; i < reserveWeapons.Count; i++)
        {
            List<ThingComp> comps = reserveWeapons[i].AllComps;
            for (int j = 0; j < comps.Count; j++)
            {
                if (comps[j] is IReloadableComp reloadable && reloadable.NeedsReload(allowForcedReload))
                {
                    __result = reloadable;
                    return;
                }
            }
        }
    }
}
