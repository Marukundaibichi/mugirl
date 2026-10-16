using Mugirl.Features.Mounting;
using Verse.AI;
using Verse.Sound;
using System.Threading;
using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.WeaponWheel;
internal sealed class WeaponWheelCombat
{
    private readonly CompWeaponWheel owner;
    internal WeaponWheelCombat(CompWeaponWheel owner)
    {
        this.owner = owner;
    }

    internal WeaponWheelCombatState combatState = WeaponWheelCombatState.Holstered;
    private int stateStartTick = -1;
    private int stateDurationTicks;
    internal int cooldownUntilTick = -1;
    internal int accumulatedCooldownTicks;
    private bool cycleActive;
    private LocalTargetInfo cycleTarget = LocalTargetInfo.Invalid;
    internal Job? combatSourceJob;
    internal ThingWithComps? animationOutgoingWeapon;
    internal ThingWithComps? animationIncomingWeapon;
    private float animationAimAngle;
    private bool animationSnapSoundPlayed;
    private ThingWithComps? aimHandoffWeapon;
    private float aimHandoffAngle;
    private int aimHandoffUntilTick = -1;
    private LocalTargetInfo pendingCastTarget = LocalTargetInfo.Invalid;
    private LocalTargetInfo pendingCastDestination = LocalTargetInfo.Invalid;
    private bool pendingSurpriseAttack;
    private bool pendingCanHitNonTargetPawns = true;
    private bool pendingPreventFriendlyFire;
    private bool pendingNonInterruptingSelfCast;
    internal bool autoloadingDirty = true;
    private bool autoloadingCacheValue;
    internal bool IsFullFirepowerEligible()
    {
        return IsFullFirepowerEligibleCore() && !WeaponWheelHooks.Current.IsEligibilityVetoed(owner.Pawn);
    }

    private bool IsFullFirepowerEligibleCore()
    {
        owner.storage.EnsureCollections();
        Pawn? pawn = owner.Pawn;
        if (pawn?.equipment == null || IsCombatDisabledByMount() || pawn.WorkTagIsDisabled(WorkTags.Violent))
        {
            return false;
        }

        if (owner.storage.slots[0].Weapon == null)
        {
            return false;
        }

        int weaponCount = 0;
        for (int i = 0; i < owner.storage.slots.Count; i++)
        {
            ThingWithComps? weapon = owner.storage.slots[i].Weapon;
            if (!owner.storage.slots[i].Unlocked || weapon == null)
            {
                continue;
            }

            weaponCount++;
            Verb? verb = WeaponWheelCombat.PrimaryVerbFor(weapon);
            if (!weapon.def.IsRangedWeapon || verb?.verbProps == null)
            {
                return false;
            }
        }

        return weaponCount >= 2;
    }

    internal WeaponWheelStatus EvaluateStatus()
    {
        bool swordDance = owner.swordDance.IsSwordDanceEligible();
        bool fullFirepower = !swordDance && IsFullFirepowerEligible();
        return new WeaponWheelStatus(swordDance, fullFirepower);
    }

    internal bool HandleBeforeTryStartCast(Verb verb, LocalTargetInfo castTarget, LocalTargetInfo destinationTarget, bool surpriseAttack, bool canHitNonTargetPawns, bool preventFriendlyFire, bool nonInterruptingSelfCast, out bool result)
    {
        result = false;
        if (WeaponWheelHooks.Current.ShouldPassCastThrough(owner.Pawn))
        {
            return false;
        }

        if (WeaponWheelHooks.Current.ShouldBypassCombat(owner.Pawn))
        {
            WeaponWheelDevLog.CastDecision(owner, verb, "TryStartCastOn: bypassed by training facility compatibility");
            return false;
        }

        if (!IsCurrentWheelVerb(verb))
        {
            WeaponWheelDevLog.CastDecision(owner, verb, "TryStartCastOn: ignored, verb is not the current wheel primary verb");
            return false;
        }

        if (WeaponWheelWarmupScope.IsSkipping(verb))
        {
            WeaponWheelDevLog.CastDecision(owner, verb, "TryStartCastOn: wheel-initiated cast (warmup skip scope), passing through");
            return false;
        }

        if (IsCombatDisabledByMount())
        {
            WeaponWheelDevLog.CastDecision(owner, verb, "TryStartCastOn: ignored, combat disabled by mount");
            return false;
        }

        if (combatState == WeaponWheelCombatState.CycleCooldown && WeaponWheelCombat.CurrentTick < cooldownUntilTick)
        {
            if (WeaponWheelDevLog.Enabled)
            {
                WeaponWheelDevLog.CastDecision(owner, verb, "TryStartCastOn: blocked, waiting for cycle cooldown until tick " + cooldownUntilTick.ToStringCached());
            }

            return true;
        }

        if (combatState is WeaponWheelCombatState.Switching or WeaponWheelCombatState.SwordDanceSwitching)
        {
            WeaponWheelDevLog.CastDecision(owner, verb, "TryStartCastOn: blocked, switch animation in progress");
            return true;
        }

        if (combatState == WeaponWheelCombatState.EquippingPrimary)
        {
            WeaponWheelDevLog.CastDecision(owner, verb, "TryStartCastOn: deferred, equip animation in progress (cast remembered)");
            RememberPendingCast(castTarget, destinationTarget, surpriseAttack, canHitNonTargetPawns, preventFriendlyFire, nonInterruptingSelfCast);
            return true;
        }

        bool openlyHeld = PawnRenderUtility.CarryWeaponOpenly(owner.Pawn);
        if (!wasWeaponOpenlyHeld || !openlyHeld || combatState == WeaponWheelCombatState.Holstered)
        {
            WeaponWheelDevLog.CastDecision(owner, verb, "TryStartCastOn: deferred, starting primary equip animation (cast remembered)");
            wasWeaponOpenlyHeld = true;
            RememberPendingCast(castTarget, destinationTarget, surpriseAttack, canHitNonTargetPawns, preventFriendlyFire, nonInterruptingSelfCast);
            BeginPrimaryEquipAnimation(castTarget);
            return true;
        }

        WeaponWheelDevLog.CastDecision(owner, verb, "TryStartCastOn: passing through to vanilla warmup");
        return false;
    }

    internal void NotifyCastStarted(Verb verb, bool started)
    {
        if (WeaponWheelHooks.Current.ShouldBypassCombat(owner.Pawn) || !started || !IsCurrentWheelVerb(verb) || IsCombatDisabledByMount())
        {
            return;
        }

        if (combatState is not (WeaponWheelCombatState.Switching or WeaponWheelCombatState.SwordDanceSwitching or WeaponWheelCombatState.CycleCooldown))
        {
            combatState = WeaponWheelCombatState.Firing;
        }

        WeaponWheelDevLog.CastDecision(owner, verb, "cast started");
    }

    internal void NotifyBurstCompleted(Verb verb)
    {
        if (WeaponWheelHooks.Current.ShouldBypassCombat(owner.Pawn) || !IsCurrentWheelVerb(verb))
        {
            WeaponWheelDevLog.CastDecision(owner, verb, "BurstCompleted: ignored (training facility bypass or not the current wheel verb)");
            return;
        }

        if (!IsFullFirepowerEligible())
        {
            if (WeaponWheelDevLog.Enabled)
            {
                WeaponWheelDevLog.CastDecision(owner, verb, "BurstCompleted: full firepower NOT eligible, no cycling" + WeaponWheelDevLog.DescribeSlots(owner, verb.CurrentTarget));
            }

            combatState = RestingState;
            cycleActive = false;
            accumulatedCooldownTicks = 0;
            combatSourceJob = null;
            return;
        }

        LocalTargetInfo target = verb.CurrentTarget;
        if (!cycleActive)
        {
            if (owner.storage.activeSlotIndex != 0)
            {
                WeaponWheelDevLog.CastDecision(owner, verb, "BurstCompleted: not on slot 0 outside a cycle, finishing with cooldown");
                FinishCycleWithCooldown(verb, target);
                return;
            }

            cycleActive = true;
            cycleTarget = target;
            combatSourceJob = owner.Pawn?.CurJob;
            accumulatedCooldownTicks = 0;
            if (WeaponWheelDevLog.Enabled)
            {
                WeaponWheelDevLog.CastDecision(owner, verb, "BurstCompleted: cycle started, target=" + target);
            }
        }

        accumulatedCooldownTicks += Math.Max(0, verb.verbProps.AdjustedCooldownTicks(verb, owner.Pawn));
        int nextSlot = FindNextFireableSlot(owner.storage.activeSlotIndex + 1, cycleTarget);
        if (nextSlot >= 0)
        {
            if (WeaponWheelDevLog.Enabled)
            {
                WeaponWheelDevLog.CastDecision(owner, verb, "BurstCompleted: switching to slot " + nextSlot.ToStringCached());
            }

            BeginSwitchTo(nextSlot, cycleTarget, verb);
            return;
        }

        if (HasAutoloadingSystem && IsTargetStillValid(cycleTarget) && CanSlotFireAt(0, cycleTarget))
        {
            WeaponWheelDevLog.CastDecision(owner, verb, "BurstCompleted: autoloading system active, restarting from slot 0");
            accumulatedCooldownTicks = 0;
            BeginSwitchTo(0, cycleTarget, verb);
            return;
        }

        if (WeaponWheelDevLog.Enabled)
        {
            WeaponWheelDevLog.CastDecision(owner, verb, "BurstCompleted: no next fireable slot, entering cycle cooldown (" + accumulatedCooldownTicks.ToStringCached() + " ticks)" + WeaponWheelDevLog.DescribeSlots(owner, cycleTarget));
        }

        FinishCycleWithCooldown(verb, cycleTarget);
    }

    internal bool ShouldSuppressVanillaWeaponDraw(Thing? weapon)
    {
        if (weapon == null || weapon != owner.Pawn?.equipment?.Primary)
        {
            return false;
        }

        return WeaponWheelCombat.IsTransitionState(combatState) || owner.swordDance.IsDashHoldingWeapon || (weapon == aimHandoffWeapon && WeaponWheelCombat.CurrentTick <= aimHandoffUntilTick);
    }

    internal bool IsCombatDisabledByMount() => IsCombatDisabledByMount(WeaponWheelCombat.CurrentTick);
    private bool IsCombatDisabledByMount(int currentTick)
    {
        if (mountStateCacheTick == currentTick)
        {
            return mountStateCacheValue;
        }

        mountStateCacheTick = currentTick;
        Pawn? pawn = owner.Pawn;
        if (pawn == null)
        {
            mountStateCacheValue = false;
            return false;
        }

        CompMount? carrierComp = pawn.TryGetComp<CompMount>();
        mountStateCacheValue = carrierComp?.HasMountedPawn == true || MountUtility.IsMounted(pawn, out _);
        return mountStateCacheValue;
    }

    internal void MaintainCombatFacing()
    {
        if (owner.swordDance.MaintainSwordDanceDashFacing())
        {
            return;
        }

        LocalTargetInfo target = LocalTargetInfo.Invalid;
        if (cycleActive && IsTargetStillValid(cycleTarget))
        {
            target = cycleTarget;
        }
        else if (combatState == WeaponWheelCombatState.EquippingPrimary && IsTargetStillValid(pendingCastTarget))
        {
            target = pendingCastTarget;
        }

        Pawn? pawn = owner.Pawn;
        if (pawn?.rotationTracker != null && target.IsValid)
        {
            pawn.rotationTracker.FaceTarget(target);
        }
    }

    internal bool HasAutoloadingSystem
    {
        get
        {
            if (!autoloadingDirty)
            {
                return autoloadingCacheValue;
            }

            bool value = ComputeHasAutoloadingSystem();
            if (UnityData.IsInMainThread)
            {
                autoloadingCacheValue = value;
                autoloadingDirty = false;
            }

            return value;
        }
    }

    internal void NotifyAutoloadingApparelChanged()
    {
        autoloadingDirty = true;
    }

    internal void RefreshAutoloadingCache()
    {
        autoloadingCacheValue = ComputeHasAutoloadingSystem();
        autoloadingDirty = false;
    }

    private bool ComputeHasAutoloadingSystem()
    {
        Pawn_ApparelTracker? apparel = owner.Pawn?.apparel;
        if (apparel == null)
        {
            return false;
        }

        List<Apparel> worn = apparel.WornApparel;
        for (int i = 0; i < worn.Count; i++)
        {
            if (worn[i].TryGetComp<CompAutoloadingSystem>()?.SkipCycleReload == true)
            {
                return true;
            }
        }

        return false;
    }

    internal static int CurrentTick => TickUtility.CurrentGameTickOrFallback(0);
    internal WeaponWheelCombatState RestingState => wasWeaponOpenlyHeld ? WeaponWheelCombatState.Ready : WeaponWheelCombatState.Holstered;

    private static bool IsTransitionState(WeaponWheelCombatState state) => state is WeaponWheelCombatState.EquippingPrimary or WeaponWheelCombatState.Switching or WeaponWheelCombatState.SwordDanceSwitching;
    internal void TickMountedState(int currentTick)
    {
        bool disabled = IsCombatDisabledByMount(currentTick);
        if (disabled && !wasMountedDisabled)
        {
            AbortCombatAndRestorePrimary(true);
            combatState = WeaponWheelCombatState.DisabledMounted;
        }
        else if (!disabled && wasMountedDisabled)
        {
            combatState = PawnRenderUtility.CarryWeaponOpenly(owner.Pawn) ? WeaponWheelCombatState.Ready : WeaponWheelCombatState.Holstered;
        }

        wasMountedDisabled = disabled;
    }

    internal void TickWeaponOpenState(int currentTick)
    {
        if (IsCombatDisabledByMount(currentTick))
        {
            return;
        }

        bool openlyHeld = PawnRenderUtility.CarryWeaponOpenly(owner.Pawn);
        if (openlyHeld && !wasWeaponOpenlyHeld)
        {
            wasWeaponOpenlyHeld = true;
            if (owner.Pawn?.equipment?.Primary != null && combatState == WeaponWheelCombatState.Holstered)
            {
                BeginPrimaryEquipAnimation(LocalTargetInfo.Invalid);
            }
        }
        else if (!openlyHeld && wasWeaponOpenlyHeld)
        {
            wasWeaponOpenlyHeld = false;
            if (combatState == WeaponWheelCombatState.Ready)
            {
                combatState = WeaponWheelCombatState.Holstered;
            }
        }
    }

    internal void TickCombatState(int currentTick)
    {
        if (combatState == WeaponWheelCombatState.DisabledMounted)
        {
            return;
        }

        Pawn? pawn = owner.Pawn;
        if ((cycleActive || combatSourceJob != null) && (pawn == null || pawn.Downed || pawn.Dead))
        {
            AbortCombatAndRestorePrimary(true);
            return;
        }

        if (ShouldAbortActiveCombat())
        {
            if (WeaponWheelDevLog.Enabled)
            {
                WeaponWheelDevLog.CastDecision(owner, null, "TickCombatState: aborting active combat, reason: " + DevAbortReason());
            }

            StopActiveCombatAndRestorePrimary();
            return;
        }

        int elapsed = stateStartTick < 0 ? 0 : currentTick - stateStartTick;
        if (WeaponWheelCombat.IsTransitionState(combatState) && !animationSnapSoundPlayed && elapsed >= CurrentAnimationSnapTick)
        {
            animationSnapSoundPlayed = true;
            if (pawn != null)
            {
                animationIncomingWeapon?.def?.soundInteract?.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
            }
        }

        switch (combatState)
        {
            case WeaponWheelCombatState.EquippingPrimary when elapsed >= stateDurationTicks:
                FinishPrimaryEquipAnimation();
                break;
            case WeaponWheelCombatState.Switching when elapsed >= stateDurationTicks:
                FinishSwitchAnimationAndFire();
                break;
            case WeaponWheelCombatState.SwordDanceSwitching when elapsed >= stateDurationTicks:
                owner.swordDance.FinishSwordDanceSwitchAnimation();
                break;
            case WeaponWheelCombatState.CycleCooldown when currentTick >= cooldownUntilTick:
                ClearCycle();
                accumulatedCooldownTicks = 0;
                cooldownUntilTick = -1;
                owner.storage.NormalizeToPrimarySlot();
                combatState = RestingState;
                break;
            case WeaponWheelCombatState.Firing:
                Verb? verb = WeaponWheelCombat.PrimaryVerbFor(pawn?.equipment?.Primary);
                bool matchingBusyStance = pawn?.stances?.curStance is Stance_Busy busy && busy.verb == verb;
                if (verb == null || (verb.state == VerbState.Idle && !matchingBusyStance))
                {
                    combatState = RestingState;
                }

                break;
        }
    }

    // 进入抬枪/换枪动画：记录进出武器、瞄准角与起始 tick。三种过渡状态共用。
    private void BeginTransitionAnimation(WeaponWheelCombatState state, ThingWithComps? outgoing, ThingWithComps? incoming, LocalTargetInfo target)
    {
        animationOutgoingWeapon = outgoing;
        animationIncomingWeapon = incoming;
        animationAimAngle = AimAngleFor(target);
        animationSnapSoundPlayed = false;
        stateStartTick = WeaponWheelCombat.CurrentTick;
        stateDurationTicks = Math.Max(1, owner.Props.equipAnimationTicks);
        combatState = state;
    }

    internal void ClearTransitionAnimation()
    {
        animationOutgoingWeapon = null;
        animationIncomingWeapon = null;
        animationSnapSoundPlayed = false;
        stateStartTick = -1;
        stateDurationTicks = 0;
    }

    internal void ClearCycle()
    {
        cycleActive = false;
        cycleTarget = LocalTargetInfo.Invalid;
        combatSourceJob = null;
    }

    internal void ClearAimHandoff()
    {
        aimHandoffWeapon = null;
        aimHandoffUntilTick = -1;
    }

    private void BeginPrimaryEquipAnimation(LocalTargetInfo target)
    {
        ThingWithComps? primary = owner.Pawn?.equipment?.Primary;
        if (primary == null || IsCombatDisabledByMount())
        {
            return;
        }

        BeginTransitionAnimation(WeaponWheelCombatState.EquippingPrimary, null, primary, target);
        owner.MarkBackWeaponsDirty();
    }

    private void FinishPrimaryEquipAnimation()
    {
        if (pendingCastTarget.IsValid)
        {
            BeginAimHandoff(animationIncomingWeapon, animationAimAngle);
        }

        animationIncomingWeapon = null;
        stateStartTick = -1;
        stateDurationTicks = 0;
        combatState = WeaponWheelCombatState.Ready;
        owner.MarkBackWeaponsDirty();
        if (!pendingCastTarget.IsValid)
        {
            ClearPendingCast();
            combatSourceJob = null;
            return;
        }

        Verb? verb = WeaponWheelCombat.PrimaryVerbFor(owner.Pawn?.equipment?.Primary);
        LocalTargetInfo castTarget = pendingCastTarget;
        LocalTargetInfo destination = pendingCastDestination;
        bool surprise = pendingSurpriseAttack;
        bool canHitOthers = pendingCanHitNonTargetPawns;
        bool preventFriendly = pendingPreventFriendlyFire;
        bool nonInterrupting = pendingNonInterruptingSelfCast;
        ClearPendingCast();
        verb?.TryStartCastOn(castTarget, destination, surprise, canHitOthers, preventFriendly, nonInterrupting);
    }

    // 火力满载换到下一把枪；剑舞换手走 BeginSwordDanceSwitchTo（先做槽位轮转）。
    private void BeginSwitchTo(int slotIndex, LocalTargetInfo target, Verb completedVerb)
    {
        ClearAimHandoff();
        ThingWithComps? outgoing = owner.Pawn?.equipment?.Primary;
        ThingWithComps? incoming = owner.storage.WeaponAt(slotIndex);
        if (incoming == null || !owner.storage.MoveActiveWeaponTo(slotIndex))
        {
            if (WeaponWheelDevLog.Enabled)
            {
                WeaponWheelDevLog.CastDecision(owner, completedVerb, "BeginSwitchTo: weapon transfer to slot " + slotIndex.ToStringCached() + " FAILED (equipment tracker or container rejected the move), ending cycle");
            }

            FinishCycleWithCooldown(completedVerb, target);
            return;
        }

        EnterSwitchAnimation(WeaponWheelCombatState.Switching, outgoing, incoming, target, completedVerb);
    }

    internal void EnterSwitchAnimation(WeaponWheelCombatState state, ThingWithComps? outgoing, ThingWithComps incoming, LocalTargetInfo target, Verb completedVerb)
    {
        Pawn_StanceTracker? stances = owner.Pawn?.stances;
        if (stances != null && stances.curStance is Stance_Busy busy && busy.verb == completedVerb)
        {
            stances.SetStance(new Stance_Mobile());
        }

        BeginTransitionAnimation(state, outgoing, incoming, target);
        Verb? incomingVerb = WeaponWheelCombat.PrimaryVerbFor(incoming);
        if (stances != null && incomingVerb != null)
        {
            stances.SetStance(new Stance_Cooldown(stateDurationTicks, target, incomingVerb));
        }

        owner.MarkBackWeaponsDirty();
    }

    internal ThingWithComps? EndSwitchAnimation()
    {
        ThingWithComps? incoming = animationIncomingWeapon;
        BeginAimHandoff(incoming, animationAimAngle);
        animationOutgoingWeapon = null;
        animationIncomingWeapon = null;
        stateStartTick = -1;
        stateDurationTicks = 0;
        return incoming;
    }

    private void FinishSwitchAnimationAndFire()
    {
        ThingWithComps? incoming = EndSwitchAnimation();
        owner.MarkBackWeaponsDirty();
        Verb? verb = WeaponWheelCombat.PrimaryVerbFor(incoming);
        if (verb == null || !IsTargetStillValid(cycleTarget))
        {
            WeaponWheelDevLog.CastDecision(owner, verb, "FinishSwitch: no primary verb or target no longer valid, ending cycle");
            FinishCycleWithCooldown(verb, cycleTarget);
            return;
        }

        combatState = WeaponWheelCombatState.Firing;
        bool started;
        using (new WeaponWheelWarmupScope(verb))
        {
            started = verb.TryStartCastOn(cycleTarget, LocalTargetInfo.Invalid, false, true, false, false);
        }

        if (!started)
        {
            WeaponWheelDevLog.CastDecision(owner, verb, "FinishSwitch: TryStartCastOn returned FALSE for the next weapon, ending cycle");
            FinishCycleWithCooldown(verb, cycleTarget);
        }
        else if (combatState == WeaponWheelCombatState.Firing && verb.state == VerbState.Idle)
        {
            // TryStartCastOn 可以返回 true，但特殊 Verb 仍可能在第一发失败；此时没有 burst 完成回调可负责收尾。
            WeaponWheelDevLog.CastDecision(owner, verb, "FinishSwitch: cast started but verb went idle immediately (special verb?), ending cycle");
            FinishCycleWithCooldown(verb, cycleTarget);
        }
        else
        {
            WeaponWheelDevLog.CastDecision(owner, verb, "FinishSwitch: next weapon firing");
        }
    }

    private void FinishCycleWithCooldown(Verb? lastVerb, LocalTargetInfo target)
    {
        animationOutgoingWeapon = null;
        animationIncomingWeapon = null;
        owner.MarkBackWeaponsDirty();
        owner.storage.NormalizeToPrimarySlot();
        ClearCycle();
        int currentTick = WeaponWheelCombat.CurrentTick;
        int cooldown = Math.Max(1, accumulatedCooldownTicks);
        cooldownUntilTick = currentTick + cooldown;
        stateStartTick = currentTick;
        stateDurationTicks = cooldown;
        combatState = WeaponWheelCombatState.CycleCooldown;
        Pawn? pawn = owner.Pawn;
        if (pawn?.stances != null)
        {
            Verb? stanceVerb = lastVerb ?? WeaponWheelCombat.PrimaryVerbFor(pawn.equipment?.Primary);
            pawn.stances.SetStance(new Stance_Cooldown(cooldown, target, stanceVerb));
        }
    }

    private void StopActiveCombatAndRestorePrimary()
    {
        if (cycleActive && accumulatedCooldownTicks > 0 && !HasAutoloadingSystem)
        {
            FinishCycleWithCooldown(WeaponWheelCombat.PrimaryVerbFor(owner.Pawn?.equipment?.Primary), cycleTarget);
            return;
        }

        AbortCombatAndRestorePrimary(true);
    }

    internal void AbortCombatAndRestorePrimary(bool cancelStance)
    {
        Pawn? pawn = owner.Pawn;
        if (cancelStance && pawn?.stances?.curStance is Stance_Busy busy && owner.storage.ContainsWeapon(busy.verb?.EquipmentSource))
        {
            busy.verb?.Reset();
            pawn.stances.SetStance(new Stance_Mobile());
        }

        ClearCycle();
        accumulatedCooldownTicks = 0;
        cooldownUntilTick = -1;
        animationOutgoingWeapon = null;
        animationIncomingWeapon = null;
        animationSnapSoundPlayed = false;
        ClearAimHandoff();
        ClearPendingCast();
        owner.swordDance.ClearPendingSwordDance();
        owner.storage.NormalizeToPrimarySlot();
        combatState = RestingState;
        owner.MarkBackWeaponsDirty();
    }

    private int FindNextFireableSlot(int startIndex, LocalTargetInfo target)
    {
        for (int i = Math.Max(0, startIndex); i < owner.storage.slots.Count; i++)
        {
            if (CanSlotFireAt(i, target))
            {
                return i;
            }
        }

        return -1;
    }

    private bool CanSlotFireAt(int slotIndex, LocalTargetInfo target)
    {
        if (slotIndex < 0 || slotIndex >= owner.storage.slots.Count || !owner.storage.slots[slotIndex].Unlocked)
        {
            return false;
        }

        ThingWithComps? weapon = owner.storage.slots[slotIndex].Weapon;
        Verb? verb = WeaponWheelCombat.PrimaryVerbFor(weapon);
        return weapon != null && weapon.def.IsRangedWeapon && verb != null && verb.Available() && IsTargetStillValid(target) && verb.CanHitTarget(target);
    }

    private bool IsTargetStillValid(LocalTargetInfo target)
    {
        if (!target.IsValid)
        {
            return false;
        }

        Map? map = owner.Pawn?.Map;
        if (!target.HasThing)
        {
            return map != null && target.Cell.InBounds(map);
        }

        Thing? thing = target.Thing;
        if (thing == null || thing.Destroyed || !thing.Spawned || thing.Map != map)
        {
            return false;
        }

        return thing is not Pawn targetPawn || (!targetPawn.Dead && !targetPawn.Downed && !targetPawn.IsPsychologicallyInvisible());
    }

    private bool ShouldAbortActiveCombat()
    {
        Pawn? pawn = owner.Pawn;
        if (combatSourceJob != null && pawn?.CurJob != combatSourceJob)
        {
            return true;
        }

        if (!cycleActive)
        {
            return false;
        }

        if (!IsTargetStillValid(cycleTarget))
        {
            return true;
        }

        return combatSourceJob?.def == JobDefOf.Wait_Combat && pawn?.drafter != null && !pawn.drafter.FireAtWill;
    }

    internal static Verb? PrimaryVerbFor(ThingWithComps? weapon)
    {
        return weapon?.GetComp<CompEquippable>()?.PrimaryVerb;
    }

    private bool IsCurrentWheelVerb(Verb? verb)
    {
        ThingWithComps? equipment = verb?.EquipmentSource;
        return equipment != null && equipment == owner.Pawn?.equipment?.Primary && owner.storage.ContainsWeapon(equipment);
    }

    internal bool TryCorrectAttackVerbSelection(Verb? selectedVerb, bool allowManualCastWeapons, out Verb? correctedVerb)
    {
        correctedVerb = selectedVerb;
        Pawn? pawn = owner.Pawn;
        ThingWithComps? selectedEquipment = selectedVerb?.EquipmentSource;
        ThingWithComps? primary = pawn?.equipment?.Primary;
        if (pawn == null || selectedEquipment == null || selectedEquipment == primary || !owner.storage.ContainsWeapon(selectedEquipment))
        {
            return false;
        }

        Verb? primaryVerb = WeaponWheelCombat.PrimaryVerbFor(primary);
        bool primaryUsable = primaryVerb != null && primaryVerb.verbProps != null && primaryVerb.Available() && (!primaryVerb.verbProps.onlyManualCast || (pawn.CurJob != null && pawn.CurJob.def != JobDefOf.Wait_Combat) || allowManualCastWeapons);
        correctedVerb = primaryUsable ? primaryVerb : null;
        return true;
    }

    private float AimAngleFor(LocalTargetInfo target)
    {
        Pawn? pawn = owner.Pawn;
        if (pawn == null)
        {
            return 0f;
        }

        if (target.IsValid)
        {
            Vector3 targetPos = (target.HasThing ? target.Thing.DrawPos : target.Cell.ToVector3Shifted()).ToNumerics();
            Vector3 difference = targetPos - pawn.DrawPos.ToNumerics();
            if (UnityVector.MagnitudeHorizontalSquared(difference) > 0.001f)
            {
                return UnityVector.AngleFlat(difference);
            }
        }

        return pawn.Rotation.AsAngle;
    }

    private void RememberPendingCast(LocalTargetInfo castTarget, LocalTargetInfo destinationTarget, bool surpriseAttack, bool canHitNonTargetPawns, bool preventFriendlyFire, bool nonInterruptingSelfCast)
    {
        combatSourceJob = owner.Pawn?.CurJob;
        pendingCastTarget = castTarget;
        pendingCastDestination = destinationTarget;
        pendingSurpriseAttack = surpriseAttack;
        pendingCanHitNonTargetPawns = canHitNonTargetPawns;
        pendingPreventFriendlyFire = preventFriendlyFire;
        pendingNonInterruptingSelfCast = nonInterruptingSelfCast;
    }

    internal void ClearPendingCast()
    {
        pendingCastTarget = LocalTargetInfo.Invalid;
        pendingCastDestination = LocalTargetInfo.Invalid;
        pendingSurpriseAttack = false;
        pendingCanHitNonTargetPawns = true;
        pendingPreventFriendlyFire = false;
        pendingNonInterruptingSelfCast = false;
    }

    private void BeginAimHandoff(ThingWithComps? weapon, float aimAngle)
    {
        aimHandoffWeapon = weapon;
        aimHandoffAngle = aimAngle;
        aimHandoffUntilTick = WeaponWheelCombat.CurrentTick + 1;
    }

    internal string DevDescribeCombat()
    {
        Pawn? pawn = owner.Pawn;
        return "state=" + combatState + " activeSlot=" + owner.storage.activeSlotIndex.ToStringCached() + " cycleActive=" + cycleActive + " cycleTarget=" + (cycleTarget.IsValid ? cycleTarget.ToString() : "none") + " eligible=" + IsFullFirepowerEligible() + " autoloading=" + HasAutoloadingSystem + " openlyHeld=" + wasWeaponOpenlyHeld + " job=" + (pawn?.CurJob?.def?.defName ?? "none") + " fireAtWill=" + (pawn?.drafter == null ? "n/a" : pawn.drafter.FireAtWill.ToString()) + " primary=" + (pawn?.equipment?.Primary?.def?.defName ?? "none");
    }

    private string DevAbortReason()
    {
        Pawn? pawn = owner.Pawn;
        if (combatSourceJob != null && pawn?.CurJob != combatSourceJob)
        {
            return "job changed (was " + (combatSourceJob.def?.defName ?? "unknown") + ", now " + (pawn?.CurJob?.def?.defName ?? "none") + "; another mod interrupting jobs breaks the cycle)";
        }

        if (cycleActive && !IsTargetStillValid(cycleTarget))
        {
            return "cycle target no longer valid (" + cycleTarget + ")";
        }

        if (cycleActive && combatSourceJob?.def == JobDefOf.Wait_Combat && pawn?.drafter != null && !pawn.drafter.FireAtWill)
        {
            return "fire-at-will disabled while drafted";
        }

        return "unknown";
    }

    // 抬枪动画的“卡入”时刻；换枪与抬枪分别配置。Unity 语义的 Clamp 在 min > max 时不抛异常。
    private int CurrentAnimationSnapTick => UnityMathf.Clamp(combatState is WeaponWheelCombatState.Switching or WeaponWheelCombatState.SwordDanceSwitching ? owner.Props.switchSnapTick : owner.Props.snapTick, 0, stateDurationTicks);

    internal int mountStateCacheTick = int.MinValue;
    private bool mountStateCacheValue;
    internal bool wasWeaponOpenlyHeld;
    internal bool wasMountedDisabled;
    internal bool TryGetAnimationSnapshot(out WeaponWheelAnimationSnapshot snapshot)
    {
        snapshot = default;
        if (!WeaponWheelCombat.IsTransitionState(combatState))
        {
            return false;
        }

        int elapsed = Math.Max(0, WeaponWheelCombat.CurrentTick - stateStartTick);
        int duration = Math.Max(1, stateDurationTicks);
        int outgoingDuration = Math.Max(1, owner.Props.outgoingAnimationTicks);
        int snapTick = CurrentAnimationSnapTick;
        float aimAngle = combatState == WeaponWheelCombatState.SwordDanceSwitching ? owner.swordDance.SwordDanceDashAimAngle(animationAimAngle) : animationAimAngle;
        snapshot = new WeaponWheelAnimationSnapshot
        {
            OutgoingWeapon = animationOutgoingWeapon,
            IncomingWeapon = animationIncomingWeapon,
            Progress = UnityMathf.Clamp01((float)elapsed / duration),
            OutgoingProgress = UnityMathf.Clamp01((float)elapsed / outgoingDuration),
            AimAngle = aimAngle,
            IsSwitching = combatState is WeaponWheelCombatState.Switching or WeaponWheelCombatState.SwordDanceSwitching,
            SnapStartProgress = UnityMathf.Clamp01((float)snapTick / duration),
            SnapProgress = elapsed < snapTick || duration <= snapTick ? 0f : UnityMathf.Clamp01((float)(elapsed - snapTick + 1) / (duration - snapTick + 1))
        };
        return snapshot.IncomingWeapon != null || snapshot.OutgoingWeapon != null;
    }

    internal bool TryGetAimHandoff([NotNullWhen(true)] out ThingWithComps? weapon, out float aimAngle)
    {
        weapon = aimHandoffWeapon;
        aimAngle = aimHandoffAngle;
        return weapon != null && weapon == owner.Pawn?.equipment?.Primary && WeaponWheelCombat.CurrentTick <= aimHandoffUntilTick;
    }
}
