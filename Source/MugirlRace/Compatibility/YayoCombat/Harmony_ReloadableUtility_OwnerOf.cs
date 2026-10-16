using HarmonyLib;
using Mugirl.Features.WeaponWheel;
using RimWorld.Utility;

namespace Mugirl.Compatibility.YayoCombat;

[HarmonyPatch(typeof(ReloadableUtility), nameof(ReloadableUtility.OwnerOf))]
[MugirlPatch(PatchModule.YayoCombat, PatchRisk.Medium, "Reload jobs reject reloadable reserve weapons because their owner cannot be resolved.")]
internal static class Harmony_ReloadableUtility_OwnerOf
{
    public static bool Prepare() => YayoCombatCompatibility.Active;

    [HarmonyAfter(ModCompat.YayoCombatHarmonyId)]
    public static void Postfix(IReloadableComp? reloadable, ref Pawn? __result)
    {
        if (__result is null && reloadable?.ReloadableThing?.ParentHolder is CompWeaponWheel wheel)
        {
            __result = wheel.Pawn;
        }
    }
}
