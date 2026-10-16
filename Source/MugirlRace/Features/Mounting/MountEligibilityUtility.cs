using System.Diagnostics.CodeAnalysis;
using Mugirl.Features.Roping;

namespace Mugirl.Features.Mounting;

// 上骑条件与自动下骑条件；reasonKey 是 Keyed 翻译键，由调用方显示给玩家。
public static class MountEligibilityUtility
{
    public static bool CanMount(CompMount? comp, [NotNullWhen(true)] Pawn? rider, [NotNullWhen(false)] out string? reasonKey)
    {
        reasonKey = null;
        Pawn? carrier = comp?.MooPawn;
        if (comp is null || rider is null || carrier is null || rider == carrier)
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

        if (!carrier.Spawned || carrier.Dead || carrier.Downed || carrier.Destroyed || carrier.IsBurning() || carrier.InMentalState || !PawnPredicates.HasCapacity(carrier, PawnCapacityDefOf.Moving))
        {
            reasonKey = "Mugirl.Mount.ReasonTargetBadState";
            return false;
        }

        if (!rider.Spawned || rider.Dead || rider.Downed || rider.Destroyed || rider.IsBurning() || rider.InMentalState || !PawnPredicates.HasCapacity(rider, PawnCapacityDefOf.Moving))
        {
            reasonKey = "Mugirl.Mount.ReasonRiderBadState";
            return false;
        }

        if (!PawnPredicates.IsHumanlike(rider))
        {
            reasonKey = "Mugirl.Mount.ReasonRiderNotHumanlike";
            return false;
        }

        if (MountUtility.IsMounted(rider, out _))
        {
            reasonKey = "Mugirl.Mount.ReasonAlreadyMounted";
            return false;
        }

        if (RopingService.HasAnyRope(rider) || RopingService.HasAnyRope(carrier))
        {
            reasonKey = "Mugirl.Mount.ReasonRoped";
            return false;
        }

        return true;
    }

    public static bool ShouldAutoDismount(Pawn? rider, Pawn? carrier, [NotNullWhen(true)] out string? reasonKey)
    {
        reasonKey = null;
        if (rider is null || carrier is null)
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

        Need_Food? food = rider.needs?.food;
        if (food is not null && (food.Starving || food.CurLevelPercentage <= food.PercentageThreshUrgentlyHungry))
        {
            reasonKey = "Mugirl.Mount.ReasonRiderHungry";
            return true;
        }

        Need_Rest? rest = rider.needs?.rest;
        if (rest is not null && rest.CurLevel < Need_Rest.ThreshVeryTired)
        {
            reasonKey = "Mugirl.Mount.ReasonRiderTired";
            return true;
        }

        if (HealthAIUtility.ShouldSeekMedicalRest(rider))
        {
            reasonKey = "Mugirl.Mount.ReasonRiderMedical";
            return true;
        }

        if (RopingService.HasAnyRope(rider) || RopingService.HasAnyRope(carrier))
        {
            reasonKey = "Mugirl.Mount.ReasonRoped";
            return true;
        }

        return false;
    }
}
