using HarmonyLib;

namespace Mugirl.Features.WeaponWheel;

[HarmonyPatch(typeof(Verb_MeleeAttack), "TryCastShot")]
[MugirlPatch(PatchModule.WeaponWheel, PatchRisk.Critical, "Linked sword dance does not confirm the delayed melee strike after its original damage routine resolves.")]
public static class Harmony_Verb_MeleeAttack_TryCastShot
{
    public static void Postfix(Verb_MeleeAttack __instance)
    {
        WeaponWheelLookup.CompFor(__instance.CasterPawn)?.NotifySwordDanceStrikeResolved(__instance);
    }
}
