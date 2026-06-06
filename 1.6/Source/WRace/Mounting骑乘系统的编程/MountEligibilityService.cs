using RimWorld;
using Verse;

namespace MooGirl
{
    public static class MountEligibilityService
    {
        public static bool CanMount(Comp_MooGirlMount comp, Pawn rider, out string reasonKey)
        {
            reasonKey = null;
            Pawn carrier = comp?.MooPawn;
            if (rider == null || carrier == null || rider == carrier)
            {
                reasonKey = "MooGirl.Mount.ReasonInvalid";
                return false;
            }

            if (!MountedPawnUtility.IsMooGirl(carrier))
            {
                reasonKey = "MooGirl.Mount.ReasonTargetNotMooGirl";
                return false;
            }

            if (comp.HasMountedPawn)
            {
                reasonKey = "MooGirl.Mount.ReasonAlreadyHasRider";
                return false;
            }

            if (!carrier.Spawned || carrier.Dead || carrier.Downed || carrier.Destroyed || carrier.IsBurning() || carrier.InMentalState || !carrier.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
            {
                reasonKey = "MooGirl.Mount.ReasonTargetBadState";
                return false;
            }

            if (!rider.Spawned || rider.Dead || rider.Downed || rider.Destroyed || rider.IsBurning() || rider.InMentalState || !rider.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
            {
                reasonKey = "MooGirl.Mount.ReasonRiderBadState";
                return false;
            }

            if (rider.RaceProps?.Humanlike != true)
            {
                reasonKey = "MooGirl.Mount.ReasonRiderNotHumanlike";
                return false;
            }

            if (MountedPawnUtility.IsMounted(rider, out _))
            {
                reasonKey = "MooGirl.Mount.ReasonAlreadyMounted";
                return false;
            }

            if (MountedPawnUtility.HasAnyRope(rider) || MountedPawnUtility.HasAnyRope(carrier))
            {
                reasonKey = "MooGirl.Mount.ReasonRoped";
                return false;
            }

            return true;
        }

        public static bool ShouldAutoDismount(Pawn rider, Pawn carrier, out string reasonKey)
        {
            reasonKey = null;
            if (rider == null || carrier == null)
            {
                reasonKey = "MooGirl.Mount.ReasonInvalid";
                return true;
            }

            if (!carrier.Spawned || carrier.Destroyed || carrier.Dead || carrier.Downed || carrier.IsBurning() || carrier.InMentalState)
            {
                reasonKey = "MooGirl.Mount.ReasonTargetBadState";
                return true;
            }

            if (rider.Destroyed || rider.Dead || rider.Downed || rider.IsBurning() || rider.InMentalState)
            {
                reasonKey = "MooGirl.Mount.ReasonRiderBadState";
                return true;
            }

            Need_Food food = rider.needs?.food;
            if (food != null && (food.Starving || food.CurLevelPercentage <= food.PercentageThreshUrgentlyHungry))
            {
                reasonKey = "MooGirl.Mount.ReasonRiderHungry";
                return true;
            }

            Need_Rest rest = rider.needs?.rest;
            if (rest != null && rest.CurLevel < Need_Rest.ThreshVeryTired)
            {
                reasonKey = "MooGirl.Mount.ReasonRiderTired";
                return true;
            }

            if (HealthAIUtility.ShouldSeekMedicalRest(rider))
            {
                reasonKey = "MooGirl.Mount.ReasonRiderMedical";
                return true;
            }

            if (MountedPawnUtility.HasAnyRope(rider) || MountedPawnUtility.HasAnyRope(carrier))
            {
                reasonKey = "MooGirl.Mount.ReasonRoped";
                return true;
            }

            return false;
        }
    }
}
