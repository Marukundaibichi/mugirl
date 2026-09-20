using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl.Features.Lances
{
    internal static class LanceChargeAfterimageRenderScope
    {
        [System.ThreadStatic]
        private static bool active;

        internal static bool Active => active;

        internal static void Begin()
        {
            active = true;
        }

        internal static void End()
        {
            active = false;
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawCarriedWeapon))]
    public static class Harmony_LanceCharge_DrawCarriedWeapon
    {
        public static bool Prefix(
            ThingWithComps weapon,
            Vector3 drawPos,
            float equipmentDrawDistanceFactor)
        {
            if (LanceChargeAfterimageRenderScope.Active)
            {
                return false;
            }

            Pawn_EquipmentTracker tracker = weapon?.ParentHolder as Pawn_EquipmentTracker;
            Pawn pawn = tracker?.pawn;
            PawnFlyer_LanceCharge flyer = pawn?.ParentHolder as PawnFlyer_LanceCharge;
            if (flyer == null || !flyer.TryGetLanceAimAngle(pawn, weapon, out float aimAngle))
            {
                return true;
            }

            float forwardOffset = 0.48f + weapon.def.equippedDistanceOffset;
            drawPos += new Vector3(0f, 0f, forwardOffset).RotatedBy(aimAngle)
                * equipmentDrawDistanceFactor;
            PawnRenderUtility.DrawEquipmentAiming(weapon, drawPos, aimAngle);
            return false;
        }
    }
}
