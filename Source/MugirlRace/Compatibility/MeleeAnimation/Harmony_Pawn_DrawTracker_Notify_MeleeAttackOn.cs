using HarmonyLib;
using Mugirl.Features.WeaponWheel;

namespace Mugirl.Compatibility.MeleeAnimation;

[HarmonyPatch(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.Notify_MeleeAttackOn))]
[MugirlPatch(PatchModule.MeleeAnimation, PatchRisk.Medium, "Melee Animation can overlap its idle strike animation with a linked sword-dance weapon switch.")]
internal static class Harmony_Pawn_DrawTracker_Notify_MeleeAttackOn
{
    public static bool Prepare() => MeleeAnimationCompatibility.Active;

    [HarmonyPriority(Priority.First)]
    public static void Prefix(Pawn ___pawn, ref MeleeAnimationCompatibility.IdleAnimationState __state)
    {
        if (WeaponWheelLookup.CompFor(___pawn)?.ShouldSuppressExternalMeleeAttackAnimation() == true)
        {
            __state = MeleeAnimationCompatibility.SuspendIdleWeaponAnimation();
        }
    }

    public static void Postfix(ref MeleeAnimationCompatibility.IdleAnimationState __state)
    {
        __state.Restore();
    }

    public static Exception? Finalizer(Exception? __exception, ref MeleeAnimationCompatibility.IdleAnimationState __state)
    {
        __state.Restore();
        return __exception;
    }
}
