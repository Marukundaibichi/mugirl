using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public static partial class MountedCombatController
    {
        public static void DrawWeapon(Comp_MugirlMount comp)
        {
            Pawn carrier = comp?.MooPawn;
            if (carrier == null)
            {
                return;
            }

            DrawWeapon(comp, carrier.DrawPos);
        }

        internal static void DrawWeapon(Comp_MugirlMount comp, Vector3 carrierDrawPos)
        {
            Pawn rider = comp?.MountedPawn;
            Pawn carrier = comp?.MooPawn;
            ThingWithComps weapon = rider?.equipment?.Primary;
            if (rider == null || carrier == null || weapon == null)
            {
                return;
            }

            if (weapon.def.IsMeleeWeapon)
            {
                MountedPawnMeleeSupport.DrawWeapon(comp, carrierDrawPos);
                return;
            }

            if (!weapon.def.IsRangedWeapon)
            {
                return;
            }

            Vector3 drawPos = comp.WeaponDrawPosAt(carrierDrawPos);
            LocalTargetInfo aimTarget = comp.turretAimTarget;
            Verb mountedVerb = GetPrimaryRangedVerb(comp);
            if (!aimTarget.IsValid && mountedVerb?.state == VerbState.Bursting && mountedVerb.CurrentTarget.IsValid)
            {
                aimTarget = mountedVerb.CurrentTarget;
            }

            if (aimTarget.IsValid)
            {
                Thing targetThing = aimTarget.Thing;
                if (targetThing == null || !targetThing.Destroyed)
                {
                    float drawDistanceFactor = MountedPawnUtility.EquipmentDrawDistanceFactor(rider);
                    Vector3 targetPos = aimTarget.HasThing ? targetThing.DrawPos : aimTarget.Cell.ToVector3Shifted();
                    float aimAngle = (targetPos - comp.RiderDrawPosAt(carrierDrawPos)).AngleFlat();
                    drawPos += new Vector3(0f, 0f, 0.4f + weapon.def.equippedDistanceOffset).RotatedBy(aimAngle) * drawDistanceFactor;
                    PawnRenderUtility.DrawEquipmentAiming(weapon, drawPos, aimAngle);
                    return;
                }
            }

            PawnRenderUtility.DrawCarriedWeapon(weapon, drawPos, carrier.Rotation, MountedPawnUtility.EquipmentDrawDistanceFactor(rider));
        }
    }
}
