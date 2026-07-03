using RimWorld;
using Verse;

namespace Mugirl
{
    public static class MountEligibilityService
    {
        public static bool CanMount(Comp_MugirlMount comp, Pawn rider, out string reasonKey)
        {
            reasonKey = null;
            Pawn carrier = comp?.MooPawn;
            if (rider == null || carrier == null || rider == carrier)
            {
                reasonKey = "Mugirl.Mount.ReasonInvalid";
                return false;
            }

            if (!MountedPawnUtility.IsMugirl(carrier))
            {
                reasonKey = "Mugirl.Mount.ReasonTargetNotMugirl";
                return false;
            }

            if (comp.HasMountedPawn)
            {
                reasonKey = "Mugirl.Mount.ReasonAlreadyHasRider";
                return false;
            }

            if (!carrier.Spawned || carrier.Dead || carrier.Downed || carrier.Destroyed || carrier.IsBurning() || carrier.InMentalState || !MountedPawnUtility.HasCapacity(carrier, PawnCapacityDefOf.Moving))
            {
                reasonKey = "Mugirl.Mount.ReasonTargetBadState";
                return false;
            }

            if (!rider.Spawned || rider.Dead || rider.Downed || rider.Destroyed || rider.IsBurning() || rider.InMentalState || !MountedPawnUtility.HasCapacity(rider, PawnCapacityDefOf.Moving))
            {
                reasonKey = "Mugirl.Mount.ReasonRiderBadState";
                return false;
            }

            if (!MountedPawnUtility.IsHumanlike(rider))
            {
                reasonKey = "Mugirl.Mount.ReasonRiderNotHumanlike";
                return false;
            }

            if (MountedPawnUtility.IsMounted(rider, out _))
            {
                reasonKey = "Mugirl.Mount.ReasonAlreadyMounted";
                return false;
            }

            if (MountedPawnUtility.HasAnyRope(rider) || MountedPawnUtility.HasAnyRope(carrier))
            {
                reasonKey = "Mugirl.Mount.ReasonRoped";
                return false;
            }

            return true;
        }

        public static bool ShouldAutoDismount(Pawn rider, Pawn carrier, out string reasonKey)
        {
            reasonKey = null;
            if (rider == null || carrier == null)
            {
                reasonKey = "Mugirl.Mount.ReasonInvalid";
                return true;
            }

            if (!carrier.Spawned || carrier.Destroyed || carrier.Dead || carrier.Downed || carrier.IsBurning() || carrier.InMentalState)
            {
                reasonKey = "Mugirl.Mount.ReasonTargetBadState";
                return true;
            }

            if (rider.Destroyed || rider.Dead || rider.Downed || rider.IsBurning() || rider.InMentalState)
            {
                reasonKey = "Mugirl.Mount.ReasonRiderBadState";
                return true;
            }

            Need_Food food = rider.needs?.food;
            if (food != null && (food.Starving || food.CurLevelPercentage <= food.PercentageThreshUrgentlyHungry))
            {
                reasonKey = "Mugirl.Mount.ReasonRiderHungry";
                return true;
            }

            Need_Rest rest = rider.needs?.rest;
            if (rest != null && rest.CurLevel < Need_Rest.ThreshVeryTired)
            {
                reasonKey = "Mugirl.Mount.ReasonRiderTired";
                return true;
            }

            if (HealthAIUtility.ShouldSeekMedicalRest(rider))
            {
                reasonKey = "Mugirl.Mount.ReasonRiderMedical";
                return true;
            }

            if (MountedPawnUtility.HasAnyRope(rider) || MountedPawnUtility.HasAnyRope(carrier))
            {
                reasonKey = "Mugirl.Mount.ReasonRoped";
                return true;
            }

            return false;
        }
    }
}
