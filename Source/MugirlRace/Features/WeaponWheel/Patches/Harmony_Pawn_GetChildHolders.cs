using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

[HarmonyPatch(typeof(Pawn), nameof(Pawn.GetChildHolders))]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.High, "Reserve weapons are omitted from recursive holder traversal.")]
public static class Harmony_Pawn_GetChildHolders
{
    // 原版只加入背包、搬运、装备与服装持有者，不含组件。
    public static void Postfix(Pawn __instance, List<IThingHolder>? outChildren)
    {
        CompWeaponWheel? comp = WeaponWheelLookup.CompFor(__instance);
        if (comp != null && outChildren != null && !outChildren.Contains(comp))
        {
            outChildren.Add(comp);
        }
    }
}
