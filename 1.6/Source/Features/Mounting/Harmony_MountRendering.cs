using HarmonyLib;
using UnityEngine;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DynamicDrawPhaseAt))]
    public static class Harmony_MountRendering
    {
        public static void Prefix(Pawn __instance, DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            if (__instance?.Rotation == Rot4.South)
            {
                DrawRider(__instance, phase);
            }
        }

        public static void Postfix(Pawn __instance, DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            if (__instance?.Rotation != Rot4.South)
            {
                DrawRider(__instance, phase);
            }
        }

        private static void DrawRider(Pawn pawn, DrawPhase phase)
        {
            if (phase != DrawPhase.Draw || pawn == null)
            {
                return;
            }

            Comp_MugirlMount comp = pawn.TryGetComp<Comp_MugirlMount>();
            Pawn rider = comp?.MountedPawn;
            if (rider == null || rider.Destroyed || rider.Drawer?.renderer == null)
            {
                return;
            }

            rider.Drawer.renderer.DynamicDrawPhaseAt(phase, comp.RiderDrawPos, pawn.Rotation, neverAimWeapon: true);
            MountedCombatController.DrawWeapon(comp);
        }
    }
}
