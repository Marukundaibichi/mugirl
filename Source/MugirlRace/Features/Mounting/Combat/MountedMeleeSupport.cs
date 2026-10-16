using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Verse.AI;
using Verse.Sound;

namespace Mugirl.Features.Mounting;

// 骑手近战：载具贴身作战（周期 tick）或长枪冲锋命中时，骑手用自己的近战 verb 追加一次攻击。
// 命中/闪避/音效沿用原版 Verb_MeleeAttack 的私有方法（VanillaMeleeAccess），伤害按原版公式自行构造。
public static class MountedMeleeSupport
{
    private const float DodgeTextDuration = 1.9f;

    internal static void Tick(CompMount comp)
    {
        Pawn? carrier = comp.MooPawn;
        Pawn? rider = comp.MountedPawn;
        Thing? target = CurrentMeleeTarget(carrier);
        if (!CanRiderMelee(carrier, rider, target) || TryGetMeleeVerb(rider, target) is not { } verb)
        {
            return;
        }

        RunWithMountedStance(carrier, rider, target, verb, carrierPosition: null, requireAvailable: true);
    }

    // 长枪冲锋（地面命中）调用：返回是否尝试了骑手攻击。
    public static bool TryAttackAlongCharge(CompMount? comp, Thing? target)
    {
        Pawn? carrier = comp?.MooPawn;
        Pawn? rider = comp?.MountedPawn;
        if (!CanRiderMelee(carrier, rider, target) || TryGetMeleeVerb(rider, target) is not { } verb)
        {
            return false;
        }

        RunWithMountedStance(carrier, rider, target, verb, carrierPosition: null, requireAvailable: false);
        return true;
    }

    // 长枪飞行冲锋调用：载具此时不在地图上，用冲锋格作为攻击方向的起点。
    public static bool TryAttackAlongFlyingCharge(CompMount? comp, Thing? target, Map? map, IntVec3 carrierPosition)
    {
        Pawn? carrier = comp?.MooPawn;
        Pawn? rider = comp?.MountedPawn;
        if (!CanRiderMeleeAt(carrier, rider, target, map, carrierPosition, requireSpawnedCarrier: false)
            || TryGetMeleeVerb(rider, target) is not { } verb)
        {
            return false;
        }

        RunWithMountedStance(carrier, rider, target, verb, carrierPosition, requireAvailable: false);
        return true;
    }

    // 攻击期间以载具为施放者并暂时解除载具的 busy stance，结束后恢复载具原姿态。
    private static void RunWithMountedStance(Pawn carrier, Pawn rider, Thing target, Verb verb, IntVec3? carrierPosition, bool requireAvailable)
    {
        Stance? originalStance = carrier.stances?.curStance;
        try
        {
            using (new MountedVerbScope(verb, carrier))
            {
                if (!requireAvailable || verb.Available())
                {
                    carrier.stances?.SetStance(new Stance_Mobile());
                    TryMountedMeleeAttack(rider, carrier, target, verb, carrierPosition);
                }
            }
        }
        finally
        {
            Pawn_StanceTracker? stances = carrier.stances;
            if (stances is not null && stances.curStance != originalStance)
            {
                stances.SetStance(originalStance ?? new Stance_Mobile());
            }
        }
    }

    private static void TryMountedMeleeAttack(Pawn rider, Pawn carrier, Thing target, Verb verb, IntVec3? carrierPosition)
    {
        if (verb is not Verb_MeleeAttack meleeVerb || verb.verbProps is null)
        {
            return;
        }

        if (!Rand.Chance(VanillaMeleeAccess.GetNonMissChance(meleeVerb, target)))
        {
            VanillaMeleeAccess.SoundMiss(meleeVerb)?.PlayOneShot(target);
            SetLastShotTick(verb);
            return;
        }

        if (target is Pawn targetPawn && Rand.Chance(VanillaMeleeAccess.GetDodgeChance(meleeVerb, target)))
        {
            VanillaMeleeAccess.SoundDodge(meleeVerb, target)?.PlayOneShot(target);
            MoteMaker.ThrowText(targetPawn.DrawPos, targetPawn.Map, "TextMote_Dodge".Translate(), DodgeTextDuration);
            SetLastShotTick(verb);
            return;
        }

        DamageDef damageDef = verb.verbProps.meleeDamageDef ?? DamageDefOf.Blunt;
        float damageAmount = verb.verbProps.AdjustedMeleeDamageAmount(verb.tool, rider, verb.EquipmentSource, verb.HediffCompSource);
        float armorPenetration = verb.verbProps.AdjustedArmorPenetration(verb.tool, rider, verb.EquipmentSource, verb.HediffCompSource);
        BodyPartRecord? hitPart = target is Pawn pawnTarget
            ? pawnTarget.health?.hediffSet?.GetRandomNotMissingPart(damageDef, BodyPartHeight.Undefined, BodyPartDepth.Outside)
            : null;

        DamageInfo dinfo = new(
            damageDef,
            damageAmount,
            armorPenetration,
            (target.Position - (carrierPosition ?? carrier.Position)).AngleFlat,
            rider,
            hitPart,
            verb.EquipmentSource?.def,
            DamageInfo.SourceCategory.ThingOrUnknown,
            target);
        dinfo.SetTool(verb.tool);
        target.TakeDamage(dinfo);
        SoundDef? hitSound = target is Pawn ? VanillaMeleeAccess.SoundHitPawn(meleeVerb) : VanillaMeleeAccess.SoundHitBuilding(meleeVerb);
        hitSound?.PlayOneShot(target);
        SetLastShotTick(verb);
    }

    private static void SetLastShotTick(Verb verb)
    {
        if (TickUtility.TryGetCurrentGameTick(out int currentTick))
        {
            VanillaMeleeAccess.SetLastShotTick(verb, currentTick);
        }
    }

    internal static void DrawWeapon(CompMount comp, UVector3 carrierDrawPos)
    {
        using (MountingHooks.BeginMeleeDraw(comp))
        {
            DrawWeaponCore(comp, carrierDrawPos);
        }
    }

    private static void DrawWeaponCore(CompMount comp, UVector3 carrierDrawPos)
    {
        Pawn? rider = comp.MountedPawn;
        Pawn? carrier = comp.MooPawn;
        ThingWithComps? weapon = rider?.equipment?.Primary;
        if (rider is null || carrier is null || weapon is null || !weapon.def.IsMeleeWeapon)
        {
            return;
        }

        Vector3 drawPos = comp.WeaponDrawPosAt(carrierDrawPos).ToNumerics()
            + MountDrawOffsets.WeaponSideOffset(carrier.Rotation, MountDrawOffsets.MeleeWeaponExtraSideDistance);
        PawnRenderUtility.DrawCarriedWeapon(weapon, drawPos.ToUnity(), carrier.Rotation, MountUtility.EquipmentDrawDistanceFactor(rider));
    }

    public static Thing? CurrentMeleeTarget(Pawn? carrier)
    {
        if (carrier is null)
        {
            return null;
        }

        LocalTargetInfo target = carrier.CurJob?.targetA ?? LocalTargetInfo.Invalid;
        if (carrier.CurJobDef == JobDefOf.AttackMelee && target.HasThing)
        {
            return target.Thing;
        }

        if (carrier.stances?.curStance is Stance_Busy busy && busy.verb?.verbProps?.IsMeleeAttack == true && busy.focusTarg.HasThing)
        {
            return busy.focusTarg.Thing;
        }

        return carrier.mindState?.enemyTarget ?? carrier.mindState?.meleeThreat;
    }

    private static Verb? TryGetMeleeVerb(Pawn rider, Thing target)
    {
        return rider.meleeVerbs?.TryGetMeleeVerb(target);
    }

    private static bool CanRiderMelee([NotNullWhen(true)] Pawn? carrier, [NotNullWhen(true)] Pawn? rider, [NotNullWhen(true)] Thing? target)
    {
        return CanRiderMeleeAt(
            carrier,
            rider,
            target,
            carrier?.Map,
            carrier?.Position ?? IntVec3.Invalid,
            requireSpawnedCarrier: true);
    }

    private static bool CanRiderMeleeAt(
        [NotNullWhen(true)] Pawn? carrier,
        [NotNullWhen(true)] Pawn? rider,
        [NotNullWhen(true)] Thing? target,
        Map? map,
        IntVec3 carrierPosition,
        bool requireSpawnedCarrier)
    {
        if (carrier is null || rider is null || target is null || target.Destroyed)
        {
            return false;
        }

        if ((requireSpawnedCarrier && !carrier.Spawned) || map is null
            || target.Map != map || target == carrier || target == rider)
        {
            return false;
        }

        if (!carrier.HostileTo(target) || !target.Position.AdjacentTo8WayOrInside(carrierPosition))
        {
            return false;
        }

        if (target is Pawn pawn && (pawn.Dead || pawn.Downed))
        {
            return false;
        }

        return !rider.Dead
            && !rider.Downed
            && !rider.InMentalState
            && !rider.IsBurning()
            && PawnPredicates.IsAwake(rider)
            && PawnPredicates.HasCapacity(rider, PawnCapacityDefOf.Manipulation)
            && !rider.WorkTagIsDisabled(WorkTags.Violent)
            && rider.stances?.FullBodyBusy == false;
    }
}
