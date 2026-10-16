using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Mounting;

// 骑乘远程射击的条件与骑手主武器 verb 的解析。
public static partial class MountedCombatController
{
    public static bool CanUseMountedRangedWeapon(CompMount? comp, [NotNullWhen(false)] out string? reasonKey)
    {
        reasonKey = null;
        Pawn? rider = comp?.MountedPawn;
        Pawn? carrier = comp?.MooPawn;
        if (comp is null || rider is null || carrier is null || !comp.FireAtWill)
        {
            reasonKey = "Mugirl.Mount.ReasonTurretHoldFire";
            return false;
        }

        if (!carrier.Spawned || carrier.Dead || carrier.Downed || carrier.Destroyed || carrier.IsBurning() || carrier.InMentalState || carrier.stances?.stunner?.Stunned == true)
        {
            reasonKey = "Mugirl.Mount.ReasonTargetBadState";
            return false;
        }

        if (carrier.Faction is null && !carrier.InAggroMentalState)
        {
            reasonKey = "Mugirl.Mount.ReasonTargetBadState";
            return false;
        }

        if (rider.Dead || rider.Downed || rider.InMentalState || rider.IsBurning())
        {
            reasonKey = "Mugirl.Mount.ReasonRiderBadState";
            return false;
        }

        if (!PawnPredicates.IsAwake(rider) || !PawnPredicates.HasCapacity(rider, PawnCapacityDefOf.Manipulation) || rider.WorkTagIsDisabled(WorkTags.Violent))
        {
            reasonKey = "Mugirl.Mount.ReasonRiderBadState";
            return false;
        }

        if (GetPrimaryRangedVerb(comp) is null)
        {
            reasonKey = "Mugirl.Mount.ReasonNoRangedWeapon";
            return false;
        }

        return true;
    }

    private static Verb? GetPrimaryRangedVerb(CompMount comp)
    {
        Pawn? rider = comp.MountedPawn;
        if (rider is null)
        {
            comp.cachedPrimaryWeapon = null;
            comp.cachedPrimaryVerb = null;
            return null;
        }

        // 同一主武器引用的判定结果（远程/近战/仅手动等）全部由武器 Def 与 verb 派生，
        // 可以按引用缓存；换武器、换骑手或卸下时 Primary 引用变化自动触发重算。
        ThingWithComps? primary = rider.equipment?.Primary;
        if (primary == comp.cachedPrimaryWeapon)
        {
            return comp.cachedPrimaryVerb;
        }

        Verb? verb = ResolvePrimaryRangedVerb(primary);
        comp.cachedPrimaryWeapon = primary;
        comp.cachedPrimaryVerb = verb;
        return verb;
    }

    private static Verb? GetPrimaryRangedVerb(Pawn? rider)
    {
        return ResolvePrimaryRangedVerb(rider?.equipment?.Primary);
    }

    private static Verb? ResolvePrimaryRangedVerb(ThingWithComps? weapon)
    {
        if (weapon is null || !weapon.def.IsRangedWeapon)
        {
            return null;
        }

        Verb? verb = weapon.GetComp<CompEquippable>()?.PrimaryVerb;
        if (verb?.verbProps is null || verb.verbProps.IsMeleeAttack || verb.verbProps.onlyManualCast)
        {
            return null;
        }

        if (verb is IAbilityVerb || verb.EquipmentSource != weapon)
        {
            return null;
        }

        return verb;
    }
}
