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
                DrawRider(__instance, phase, drawLoc);
            }
        }

        public static void Postfix(Pawn __instance, DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            if (__instance?.Rotation != Rot4.South)
            {
                DrawRider(__instance, phase, drawLoc);
            }
        }

        private static void DrawRider(Pawn pawn, DrawPhase phase, Vector3 carrierDrawPos)
        {
            if (phase != DrawPhase.Draw || pawn == null)
            {
                return;
            }

            // DynamicDrawPhaseAt 对全地图每个 pawn 每帧调用；先用种族判断挡掉非雪牛娘，
            // 再做 AllComps 线性扫描（Comp_MugirlMount 只存在于雪牛娘身上）。
            if (!MugirlIdentity.IsMugirlPawn(pawn))
            {
                return;
            }

            Comp_MugirlMount comp = pawn.TryGetComp<Comp_MugirlMount>();
            Pawn rider = comp?.MountedPawn;
            if (rider == null || rider.Destroyed || rider.Drawer?.renderer == null)
            {
                return;
            }

            rider.Drawer.renderer.DynamicDrawPhaseAt(
                phase,
                comp.RiderDrawPosAt(carrierDrawPos),
                pawn.Rotation,
                neverAimWeapon: true);
            MountedCombatController.DrawWeapon(comp, carrierDrawPos);
        }
    }
}
