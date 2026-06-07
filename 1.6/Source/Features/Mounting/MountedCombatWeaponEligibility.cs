using RimWorld;
using Verse;

namespace MooGirl
{
    public static partial class MountedCombatController
    {
        public static bool CanUseMountedRangedWeapon(Comp_MooGirlMount comp, out string reasonKey)
        {
            reasonKey = null;
            Pawn rider = comp?.MountedPawn;
            Pawn carrier = comp?.MooPawn;
            if (comp == null || rider == null || carrier == null || !comp.fireAtWill)
            {
                reasonKey = "MooGirl.Mount.ReasonTurretHoldFire";
                return false;
            }

            if (!carrier.Spawned || carrier.Dead || carrier.Downed || carrier.Destroyed || carrier.IsBurning() || carrier.InMentalState || carrier.stances?.stunner?.Stunned == true)
            {
                reasonKey = "MooGirl.Mount.ReasonTargetBadState";
                return false;
            }

            if (carrier.Faction == null && !carrier.InAggroMentalState)
            {
                reasonKey = "MooGirl.Mount.ReasonTargetBadState";
                return false;
            }

            if (rider.Dead || rider.Downed || rider.InMentalState || rider.IsBurning() || (carrier.pather?.Moving == true && MountedPawnMeleeSupport.CurrentMeleeTarget(carrier) == null))
            {
                reasonKey = "MooGirl.Mount.ReasonRiderBadState";
                return false;
            }

            if (!MountedPawnUtility.IsAwake(rider) || !MountedPawnUtility.HasCapacity(rider, PawnCapacityDefOf.Manipulation) || rider.WorkTagIsDisabled(WorkTags.Violent))
            {
                reasonKey = "MooGirl.Mount.ReasonRiderBadState";
                return false;
            }

            if (GetPrimaryRangedVerb(comp) == null)
            {
                reasonKey = "MooGirl.Mount.ReasonNoRangedWeapon";
                return false;
            }

            return true;
        }

        private static Verb GetPrimaryRangedVerb(Comp_MooGirlMount comp)
        {
            return GetPrimaryRangedVerb(comp?.MountedPawn);
        }

        private static Verb GetPrimaryRangedVerb(Pawn rider)
        {
            ThingWithComps weapon = rider?.equipment?.Primary;
            if (weapon == null || !weapon.def.IsRangedWeapon)
            {
                return null;
            }

            Verb verb = weapon.GetComp<CompEquippable>()?.PrimaryVerb;
            if (verb == null || verb.verbProps == null || verb.verbProps.IsMeleeAttack || verb.verbProps.onlyManualCast)
            {
                return null;
            }

            if (verb is IAbilityVerb)
            {
                return null;
            }

            if (verb.EquipmentSource != weapon)
            {
                return null;
            }

            return verb;
        }
    }
}
