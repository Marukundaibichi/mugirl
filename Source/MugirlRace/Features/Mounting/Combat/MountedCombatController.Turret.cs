namespace Mugirl.Features.Mounting;

// 骑乘炮塔状态机：骑手的远程武器以载具为施放者自动瞄准、开火与冷却。
// 持久状态由 MountTurretState 保存；verb 每 tick 推进一次，瞄准与索敌保持独立时点。
public static partial class MountedCombatController
{
    private const int DefaultTurretTickInterval = 60;

    // 由 CompMount.CompTick 每 tick 调用一次，与原版 Pawn.Tick → VerbsTick 的频率一致。
    internal static void VerbTick(CompMount comp)
    {
        if (GetPrimaryRangedVerb(comp) is not { } verb)
        {
            return;
        }

        Pawn? carrier = comp.MooPawn;
        Stance? originalStance = carrier?.stances?.curStance;
        using (MountedCasterScope(comp, verb))
        {
            verb.VerbTick();
            RestoreCarrierStanceIfMountedVerbChangedIt(carrier, verb, originalStance);
        }
    }

    public static void Tick(CompMount comp, int delta)
    {
        if (comp.Turret.aimTarget.IsValid)
        {
            return;
        }

        if (!CanUseMountedRangedWeapon(comp, out _))
        {
            CancelMountedCast(comp, GetPrimaryRangedVerb(comp));
            ClearAim(comp);
            return;
        }

        Pawn? carrier = comp.MooPawn;
        Pawn? rider = comp.MountedPawn;
        Verb? verb = GetPrimaryRangedVerb(comp);
        if (carrier is null || verb is null)
        {
            ClearAim(comp);
            return;
        }

        EnsureVerbCaster(comp, verb);
        ClearCarrierMountedStance(carrier, verb);
        if (verb.state == VerbState.Bursting)
        {
            return;
        }

        if (comp.Turret.burstCooldownTicksLeft > 0)
        {
            comp.Turret.AdvanceCooldown(delta);
            return;
        }

        if (FindBestTarget(comp, verb) is not { } target)
        {
            return;
        }

        comp.Turret.aimTarget = target;
        comp.Turret.aimTicksTotal = GetMountedAimTicks(comp, rider, verb);
        comp.Turret.aimTicksLeft = comp.Turret.aimTicksTotal;
        comp.Turret.castStartTick = -1;
        if (comp.Turret.aimTicksLeft <= 0 && !TryStartMountedCast(comp, verb, comp.Turret.aimTarget))
        {
            comp.Turret.burstCooldownTicksLeft = TurretTickInterval(comp);
        }
    }

    public static void TickAim(CompMount comp, int delta)
    {
        Pawn? carrier = comp.MooPawn;
        Verb? verb = GetPrimaryRangedVerb(comp);
        // 完全空闲时跳过健康、能力与 caster 的重复检查；
        // 仍有目标、施放、暖机或骑乘 busy stance 时必须走取消和收尾路径。
        if (IsTurretIdle(comp, carrier, verb))
        {
            return;
        }

        if (!CanUseMountedRangedWeapon(comp, out _))
        {
            CancelMountedCast(comp, verb);
            ClearAim(comp);
            return;
        }

        if (carrier is null || verb is null)
        {
            ClearAim(comp);
            return;
        }

        EnsureVerbCaster(comp, verb);
        ClearCarrierMountedStance(carrier, verb);
        if (!comp.Turret.aimTarget.IsValid || verb.state == VerbState.Bursting)
        {
            return;
        }

        if (WasMountedCastStarted(comp))
        {
            FinishMountedCast(comp, verb, comp.Turret.aimTarget);
            return;
        }

        bool pointBlankMeleeTarget = IsCurrentMeleeTarget(comp.Turret.aimTarget, carrier);
        if (!IsValidTarget(carrier, verb, comp.Turret.aimTarget.Thing, allowPointBlank: pointBlankMeleeTarget))
        {
            CancelMountedCast(comp, verb);
            ClearAim(comp);
            return;
        }

        comp.Turret.AdvanceAim(delta);
        if (comp.Turret.aimTicksLeft > 0)
        {
            return;
        }

        if (!TryStartMountedCast(comp, verb, comp.Turret.aimTarget))
        {
            comp.Turret.burstCooldownTicksLeft = TurretTickInterval(comp);
        }
    }

    private static bool IsTurretIdle(CompMount comp, Pawn? carrier, Verb? verb)
    {
        if (comp.Turret.aimTarget.IsValid || WasMountedCastStarted(comp)
            || comp.Turret.aimTicksLeft != 0 || comp.Turret.aimTicksTotal != 0)
        {
            return false;
        }

        if (verb is not null
            && (verb.state != VerbState.Idle || verb.CurrentTarget.IsValid || verb.CurrentDestination.IsValid || verb.castCompleteCallback is not null))
        {
            return false;
        }

        return !(carrier?.stances?.curStance is Stance_Busy busyStance
            && (busyStance.verb == verb || (verb is null && IsMountedVerb(busyStance.verb))));
    }

    private static bool IsCurrentMeleeTarget(LocalTargetInfo target, Pawn carrier)
    {
        return target.HasThing && target.Thing == MountedMeleeSupport.CurrentMeleeTarget(carrier);
    }

    private static bool TryStartMountedCast(CompMount comp, Verb verb, LocalTargetInfo castTarget)
    {
        if (!castTarget.IsValid)
        {
            return false;
        }

        Pawn? carrier = comp.MooPawn;
        if (carrier?.Map is null)
        {
            ClearAim(comp);
            return false;
        }

        Stance? originalStance = carrier.stances?.curStance;
        bool pointBlankMeleeTarget = IsCurrentMeleeTarget(castTarget, carrier);
        comp.Turret.castStartTick = TickUtility.CurrentGameTickOrFallback(comp.Turret.castStartTick);
        bool started;
        // 暖机由骑乘瞄准计时代替，施放时把 WarmupTime 覆盖为 0，让原版立即进入连射。
        using (MountedCasterScope(comp, verb))
        using (new MountedWarmupOverride(verb, 0f))
        using (new MountedMinRangeOverride(verb, pointBlankMeleeTarget ? 0f : null))
        {
            carrier.stances?.SetStance(new Stance_Mobile());
            started = verb.TryStartCastOn(castTarget, surpriseAttack: false, canHitNonTargetPawns: false, preventFriendlyFire: true, nonInterruptingSelfCast: true);
        }

        RestoreCarrierStanceIfMountedVerbChangedIt(carrier, verb, originalStance);
        if (!started)
        {
            ClearAim(comp);
            return false;
        }

        if (verb.state != VerbState.Bursting)
        {
            FinishMountedCast(comp, verb, castTarget);
        }

        return true;
    }

    private static void FinishMountedCast(CompMount comp, Verb verb, LocalTargetInfo castTarget)
    {
        if (verb.verbProps is null)
        {
            return;
        }

        Pawn? carrier = comp.MooPawn;
        if (carrier is null)
        {
            ClearAim(comp);
            return;
        }

        bool fired = verb.LastShotTick >= comp.Turret.castStartTick;
        ClearAim(comp);
        if (!fired)
        {
            return;
        }

        comp.Turret.lastAttackedTarget = castTarget;
        comp.Turret.lastAttackTargetTick = TickUtility.CurrentGameTickOrFallback(comp.Turret.lastAttackTargetTick);
        comp.Turret.burstCooldownTicksLeft = Math.Max(verb.verbProps.AdjustedCooldownTicks(verb, carrier), TurretTickInterval(comp));
    }

    private static int GetMountedAimTicks(CompMount comp, Pawn? rider, Verb verb)
    {
        if (verb.verbProps is null)
        {
            return 0;
        }

        // Unity 语义：第三方 verb 返回 NaN 时取 0（MathF.Max 会传播 NaN）。
        float warmupTime = UnityMathf.Max(verb.WarmupTime, 0f);
        if (rider is not null && warmupTime > 0f)
        {
            warmupTime *= rider.GetStatValue(StatDefOf.AimingDelayFactor);
        }

        return Math.Max(warmupTime.SecondsToTicks(), comp.Props?.turretMinAimTicks ?? 0);
    }

    private static int TurretTickInterval(CompMount comp)
    {
        return Math.Max(1, comp.Props?.turretTickInterval ?? DefaultTurretTickInterval);
    }

    private static void CancelMountedCast(CompMount comp, Verb? verb)
    {
        Pawn? carrier = comp.MooPawn;
        if (carrier?.stances?.curStance is Stance_Busy busyStance && (busyStance.verb == verb || (verb is null && IsMountedVerb(busyStance.verb))))
        {
            carrier.stances.SetStance(new Stance_Mobile());
            verb ??= busyStance.verb;
        }

        verb?.Reset();
    }

    private static void ClearCarrierMountedStance(Pawn carrier, Verb verb)
    {
        if (carrier.stances?.curStance is Stance_Busy busyStance && busyStance.verb == verb)
        {
            carrier.stances.SetStance(new Stance_Mobile());
        }
    }

    private static void RestoreCarrierStanceIfMountedVerbChangedIt(Pawn? carrier, Verb verb, Stance? originalStance)
    {
        Pawn_StanceTracker? stances = carrier?.stances;
        if (stances is null || originalStance is null)
        {
            return;
        }

        if (stances.curStance != originalStance && stances.curStance is Stance_Busy busyStance && busyStance.verb == verb)
        {
            stances.SetStance(originalStance);
        }
    }

    private static bool WasMountedCastStarted(CompMount comp)
    {
        return comp.Turret.castStartTick >= 0;
    }

    private static void ClearAim(CompMount comp)
    {
        comp.Turret.ClearAim();
    }
}
