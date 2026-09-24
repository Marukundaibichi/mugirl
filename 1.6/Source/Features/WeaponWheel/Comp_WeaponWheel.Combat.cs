using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Mugirl.Features.WeaponWheel
{
    public sealed partial class Comp_WeaponWheel
    {
        public string StatusLabel
        {
            get
            {
                if (IsSwordDanceEligible())
                {
                    return "Mugirl.WeaponWheel.StatusSwordDance".Translate();
                }
                if (!IsFullFirepowerEligible())
                {
                    return "Mugirl.WeaponWheel.Tab".Translate();
                }
                return "Mugirl.WeaponWheel.StatusFull".Translate();
            }
        }

        public string StatusTooltip => IsSwordDanceEligible()
            ? "Mugirl.WeaponWheel.StatusSwordDanceDesc".Translate().ToString()
            : StatusLabel;

        public bool IsFullFirepowerEligible()
        {
            EnsureCollections();
            Pawn pawn = Pawn;
            if (pawn == null || pawn.equipment == null)
            {
                return false;
            }
            if (IsCombatDisabledByMount())
            {
                return false;
            }
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                return false;
            }
            if (slots[0] == null)
            {
                return false;
            }
            int weaponCount = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!unlockedSlots[i] || slots[i] == null)
                {
                    continue;
                }
                weaponCount++;
                ThingWithComps weapon = slots[i];
                CompEquippable equippable = weapon.GetComp<CompEquippable>();
                if (!weapon.def.IsRangedWeapon || equippable?.PrimaryVerb == null || equippable.PrimaryVerb.verbProps == null)
                {
                    return false;
                }
            }
            if (weaponCount < 2)
            {
                return false;
            }
            return true;
        }

        internal bool HandleBeforeTryStartCast(
            Verb verb,
            LocalTargetInfo castTarget,
            LocalTargetInfo destinationTarget,
            bool surpriseAttack,
            bool canHitNonTargetPawns,
            bool preventFriendlyFire,
            bool nonInterruptingSelfCast,
            out bool result)
        {
            result = false;
            if (TrainingFacilityCompatibility.ShouldBypassWeaponWheelCombat(Pawn))
            {
                WeaponWheelDevLog.CastDecision(this, verb, "TryStartCastOn: bypassed by training facility compatibility");
                return false;
            }
            if (!IsCurrentWheelVerb(verb))
            {
                WeaponWheelDevLog.CastDecision(this, verb, "TryStartCastOn: ignored, verb is not the current wheel primary verb");
                return false;
            }
            if (WeaponWheelWarmupScope.IsSkipping(verb))
            {
                WeaponWheelDevLog.CastDecision(this, verb, "TryStartCastOn: wheel-initiated cast (warmup skip scope), passing through");
                return false;
            }
            if (IsCombatDisabledByMount())
            {
                WeaponWheelDevLog.CastDecision(this, verb, "TryStartCastOn: ignored, combat disabled by mount");
                return false;
            }

            if (combatState == WeaponWheelCombatState.CycleCooldown && CurrentTick < cooldownUntilTick)
            {
                if (WeaponWheelDevLog.Enabled)
                {
                    WeaponWheelDevLog.CastDecision(this, verb, "TryStartCastOn: blocked, waiting for cycle cooldown until tick " + cooldownUntilTick);
                }
                return true;
            }
            if (combatState == WeaponWheelCombatState.Switching
                || combatState == WeaponWheelCombatState.SwordDanceSwitching)
            {
                WeaponWheelDevLog.CastDecision(this, verb, "TryStartCastOn: blocked, switch animation in progress");
                return true;
            }
            if (combatState == WeaponWheelCombatState.EquippingPrimary)
            {
                WeaponWheelDevLog.CastDecision(this, verb, "TryStartCastOn: deferred, equip animation in progress (cast remembered)");
                RememberPendingCast(castTarget, destinationTarget, surpriseAttack, canHitNonTargetPawns, preventFriendlyFire, nonInterruptingSelfCast);
                return true;
            }

            bool openlyHeld = PawnRenderUtility.CarryWeaponOpenly(Pawn);
            if (!wasWeaponOpenlyHeld || !openlyHeld || combatState == WeaponWheelCombatState.Holstered)
            {
                WeaponWheelDevLog.CastDecision(this, verb, "TryStartCastOn: deferred, starting primary equip animation (cast remembered)");
                wasWeaponOpenlyHeld = true;
                RememberPendingCast(castTarget, destinationTarget, surpriseAttack, canHitNonTargetPawns, preventFriendlyFire, nonInterruptingSelfCast);
                BeginPrimaryEquipAnimation(castTarget);
                return true;
            }

            WeaponWheelDevLog.CastDecision(this, verb, "TryStartCastOn: passing through to vanilla warmup");
            return false;
        }

        internal void NotifyCastStarted(Verb verb, bool started)
        {
            if (TrainingFacilityCompatibility.ShouldBypassWeaponWheelCombat(Pawn)
                || !started || !IsCurrentWheelVerb(verb) || IsCombatDisabledByMount())
            {
                return;
            }
            if (combatState != WeaponWheelCombatState.Switching
                && combatState != WeaponWheelCombatState.SwordDanceSwitching
                && combatState != WeaponWheelCombatState.CycleCooldown)
            {
                combatState = WeaponWheelCombatState.Firing;
            }
            WeaponWheelDevLog.CastDecision(this, verb, "cast started");
        }

        internal void NotifyBurstCompleted(Verb verb)
        {
            if (TrainingFacilityCompatibility.ShouldBypassWeaponWheelCombat(Pawn)
                || !IsCurrentWheelVerb(verb))
            {
                WeaponWheelDevLog.CastDecision(this, verb, "BurstCompleted: ignored (training facility bypass or not the current wheel verb)");
                return;
            }

            if (!IsFullFirepowerEligible())
            {
                if (WeaponWheelDevLog.Enabled)
                {
                    WeaponWheelDevLog.CastDecision(this, verb, "BurstCompleted: full firepower NOT eligible, no cycling"
                        + WeaponWheelDevLog.DescribeSlots(this, verb.CurrentTarget));
                }
                combatState = wasWeaponOpenlyHeld ? WeaponWheelCombatState.Ready : WeaponWheelCombatState.Holstered;
                cycleActive = false;
                accumulatedCooldownTicks = 0;
                combatSourceJob = null;
                return;
            }

            LocalTargetInfo target = verb.CurrentTarget;
            if (!cycleActive)
            {
                if (activeSlotIndex != 0)
                {
                    WeaponWheelDevLog.CastDecision(this, verb, "BurstCompleted: not on slot 0 outside a cycle, finishing with cooldown");
                    FinishCycleWithCooldown(verb, target);
                    return;
                }
                cycleActive = true;
                cycleTarget = target;
                combatSourceJob = Pawn?.CurJob;
                accumulatedCooldownTicks = 0;
                if (WeaponWheelDevLog.Enabled)
                {
                    WeaponWheelDevLog.CastDecision(this, verb, "BurstCompleted: cycle started, target=" + target);
                }
            }

            accumulatedCooldownTicks += Mathf.Max(0, verb.verbProps.AdjustedCooldownTicks(verb, Pawn));
            int nextSlot = FindNextFireableSlot(activeSlotIndex + 1, cycleTarget);
            if (nextSlot >= 0)
            {
                if (WeaponWheelDevLog.Enabled)
                {
                    WeaponWheelDevLog.CastDecision(this, verb, "BurstCompleted: switching to slot " + nextSlot);
                }
                BeginSwitchTo(nextSlot, cycleTarget, verb);
                return;
            }

            if (HasAutoloadingSystem && IsTargetStillValid(cycleTarget) && CanSlotFireAt(0, cycleTarget))
            {
                WeaponWheelDevLog.CastDecision(this, verb, "BurstCompleted: autoloading system active, restarting from slot 0");
                accumulatedCooldownTicks = 0;
                BeginSwitchTo(0, cycleTarget, verb);
                return;
            }

            if (WeaponWheelDevLog.Enabled)
            {
                WeaponWheelDevLog.CastDecision(this, verb, "BurstCompleted: no next fireable slot, entering cycle cooldown ("
                    + accumulatedCooldownTicks + " ticks)" + WeaponWheelDevLog.DescribeSlots(this, cycleTarget));
            }
            FinishCycleWithCooldown(verb, cycleTarget);
        }

        internal bool ShouldSuppressVanillaWeaponDraw(Thing weapon)
        {
            if (weapon == null || weapon != Pawn?.equipment?.Primary)
            {
                return false;
            }
            return combatState == WeaponWheelCombatState.EquippingPrimary
                || combatState == WeaponWheelCombatState.Switching
                || combatState == WeaponWheelCombatState.SwordDanceSwitching
                || (pendingSwordDanceStrikeDelayed
                    && !pendingSwordDanceStrikeResolved
                    && swordDanceDashStartTick >= 0)
                || (weapon == aimHandoffWeapon && CurrentTick <= aimHandoffUntilTick);
        }

        internal bool IsCombatDisabledByMount()
        {
            int currentTick = CurrentTick;
            if (mountStateCacheTick == currentTick)
            {
                return mountStateCacheValue;
            }

            mountStateCacheTick = currentTick;
            Pawn pawn = Pawn;
            if (pawn == null)
            {
                mountStateCacheValue = false;
                return mountStateCacheValue;
            }
            Comp_MugirlMount carrierComp = pawn.TryGetComp<Comp_MugirlMount>();
            mountStateCacheValue = carrierComp?.HasMountedPawn == true || MountedPawnUtility.IsMounted(pawn, out _);
            return mountStateCacheValue;
        }

        internal void MaintainCombatFacing()
        {
            Pawn pawn = Pawn;
            if (MaintainSwordDanceDashFacing())
            {
                return;
            }

            LocalTargetInfo target = LocalTargetInfo.Invalid;
            if (cycleActive && IsTargetStillValid(cycleTarget))
            {
                target = cycleTarget;
            }
            else if (combatState == WeaponWheelCombatState.EquippingPrimary
                && IsTargetStillValid(pendingCastTarget))
            {
                target = pendingCastTarget;
            }

            if (pawn?.rotationTracker != null && target.IsValid)
            {
                pawn.rotationTracker.FaceTarget(target);
            }
        }

        internal bool HasAutoloadingSystem
        {
            get
            {
                int currentTick = CurrentTick;
                if (autoloadingCacheTick == currentTick)
                {
                    return autoloadingCacheValue;
                }
                autoloadingCacheValue = ComputeHasAutoloadingSystem();
                autoloadingCacheTick = currentTick;
                return autoloadingCacheValue;
            }
        }

        // CompTick 每 tick 调用一次：渲染节点在战斗与绘制路径每帧多次读取时命中同 tick 备忘，
        // 未生成或暂停期间（CompTick 不跑）getter 自动退回现算，穿着变化最迟下一 tick 生效。
        private void RefreshAutoloadingCache(int currentTick)
        {
            autoloadingCacheValue = ComputeHasAutoloadingSystem();
            autoloadingCacheTick = currentTick;
        }

        private bool ComputeHasAutoloadingSystem()
        {
            Pawn pawn = Pawn;
            if (pawn?.apparel == null)
            {
                return false;
            }
            for (int i = 0; i < pawn.apparel.WornApparelCount; i++)
            {
                CompAutoloadingSystem comp = pawn.apparel.WornApparel[i].TryGetComp<CompAutoloadingSystem>();
                if (comp?.SkipCycleReload == true)
                {
                    return true;
                }
            }
            return false;
        }

        private int CurrentTick => MugirlTickUtility.CurrentGameTickOrFallback(0);

        private void TickMountedState()
        {
            bool disabled = IsCombatDisabledByMount();
            if (disabled && !wasMountedDisabled)
            {
                AbortCombatAndRestorePrimary(true);
                combatState = WeaponWheelCombatState.DisabledMounted;
            }
            else if (!disabled && wasMountedDisabled)
            {
                combatState = PawnRenderUtility.CarryWeaponOpenly(Pawn)
                    ? WeaponWheelCombatState.Ready
                    : WeaponWheelCombatState.Holstered;
            }
            wasMountedDisabled = disabled;
        }

        private void TickWeaponOpenState()
        {
            if (IsCombatDisabledByMount())
            {
                return;
            }

            bool openlyHeld = PawnRenderUtility.CarryWeaponOpenly(Pawn);
            if (openlyHeld && !wasWeaponOpenlyHeld)
            {
                wasWeaponOpenlyHeld = true;
                if (Pawn?.equipment?.Primary != null && combatState == WeaponWheelCombatState.Holstered)
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

        private void TickCombatState()
        {
            if (combatState == WeaponWheelCombatState.DisabledMounted)
            {
                return;
            }

            if ((cycleActive || combatSourceJob != null) && (Pawn == null || Pawn.Downed || Pawn.Dead))
            {
                AbortCombatAndRestorePrimary(true);
                return;
            }

            if (ShouldAbortActiveCombat())
            {
                if (WeaponWheelDevLog.Enabled)
                {
                    WeaponWheelDevLog.CastDecision(this, null, "TickCombatState: aborting active combat, reason: " + DevAbortReason());
                }
                StopActiveCombatAndRestorePrimary();
                return;
            }

            int elapsed = stateStartTick < 0 ? 0 : CurrentTick - stateStartTick;
            if ((combatState == WeaponWheelCombatState.EquippingPrimary
                    || combatState == WeaponWheelCombatState.Switching
                    || combatState == WeaponWheelCombatState.SwordDanceSwitching)
                && !animationSnapSoundPlayed && elapsed >= CurrentAnimationSnapTick)
            {
                animationSnapSoundPlayed = true;
                animationIncomingWeapon?.def?.soundInteract?.PlayOneShot(new TargetInfo(Pawn.Position, Pawn.Map));
            }

            if (combatState == WeaponWheelCombatState.EquippingPrimary && elapsed >= stateDurationTicks)
            {
                FinishPrimaryEquipAnimation();
            }
            else if (combatState == WeaponWheelCombatState.Switching && elapsed >= stateDurationTicks)
            {
                FinishSwitchAnimationAndFire();
            }
            else if (combatState == WeaponWheelCombatState.SwordDanceSwitching && elapsed >= stateDurationTicks)
            {
                FinishSwordDanceSwitchAnimation();
            }
            else if (combatState == WeaponWheelCombatState.CycleCooldown && CurrentTick >= cooldownUntilTick)
            {
                cycleActive = false;
                cycleTarget = LocalTargetInfo.Invalid;
                combatSourceJob = null;
                accumulatedCooldownTicks = 0;
                cooldownUntilTick = -1;
                NormalizeToPrimarySlot();
                combatState = wasWeaponOpenlyHeld ? WeaponWheelCombatState.Ready : WeaponWheelCombatState.Holstered;
            }
            else if (combatState == WeaponWheelCombatState.Firing)
            {
                Verb verb = PrimaryVerbFor(Pawn?.equipment?.Primary);
                bool matchingBusyStance = Pawn?.stances?.curStance is Stance_Busy busy && busy.verb == verb;
                if (verb == null || (verb.state == VerbState.Idle && !matchingBusyStance))
                {
                    combatState = wasWeaponOpenlyHeld ? WeaponWheelCombatState.Ready : WeaponWheelCombatState.Holstered;
                }
            }
        }

        private void BeginPrimaryEquipAnimation(LocalTargetInfo target)
        {
            ThingWithComps primary = Pawn?.equipment?.Primary;
            if (primary == null || IsCombatDisabledByMount())
            {
                return;
            }

            animationOutgoingWeapon = null;
            animationIncomingWeapon = primary;
            animationAimAngle = AimAngleFor(target);
            animationSnapSoundPlayed = false;
            stateStartTick = CurrentTick;
            stateDurationTicks = Mathf.Max(1, Props.equipAnimationTicks);
            combatState = WeaponWheelCombatState.EquippingPrimary;
            MarkBackWeaponsDirty();
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
            MarkBackWeaponsDirty();

            if (!pendingCastTarget.IsValid)
            {
                ClearPendingCast();
                combatSourceJob = null;
                return;
            }

            Verb verb = PrimaryVerbFor(Pawn?.equipment?.Primary);
            LocalTargetInfo castTarget = pendingCastTarget;
            LocalTargetInfo destination = pendingCastDestination;
            bool surprise = pendingSurpriseAttack;
            bool canHitOthers = pendingCanHitNonTargetPawns;
            bool preventFriendly = pendingPreventFriendlyFire;
            bool nonInterrupting = pendingNonInterruptingSelfCast;
            ClearPendingCast();
            verb?.TryStartCastOn(castTarget, destination, surprise, canHitOthers, preventFriendly, nonInterrupting);
        }

        private void BeginSwitchTo(int slotIndex, LocalTargetInfo target, Verb completedVerb)
        {
            aimHandoffWeapon = null;
            aimHandoffUntilTick = -1;
            ThingWithComps outgoing = Pawn?.equipment?.Primary;
            ThingWithComps incoming = WeaponAt(slotIndex);
            if (incoming == null || !MoveActiveWeaponTo(slotIndex))
            {
                if (WeaponWheelDevLog.Enabled)
                {
                    WeaponWheelDevLog.CastDecision(this, completedVerb, "BeginSwitchTo: weapon transfer to slot " + slotIndex
                        + " FAILED (equipment tracker or container rejected the move), ending cycle");
                }
                FinishCycleWithCooldown(completedVerb, target);
                return;
            }

            if (Pawn?.stances != null && Pawn.stances.curStance is Stance_Busy busy && busy.verb == completedVerb)
            {
                Pawn.stances.SetStance(new Stance_Mobile());
            }

            animationOutgoingWeapon = outgoing;
            animationIncomingWeapon = incoming;
            animationAimAngle = AimAngleFor(target);
            animationSnapSoundPlayed = false;
            stateStartTick = CurrentTick;
            stateDurationTicks = Mathf.Max(1, Props.equipAnimationTicks);
            combatState = WeaponWheelCombatState.Switching;
            Verb incomingVerb = PrimaryVerbFor(incoming);
            if (Pawn?.stances != null && incomingVerb != null)
            {
                Pawn.stances.SetStance(new Stance_Cooldown(stateDurationTicks, target, incomingVerb));
            }
            MarkBackWeaponsDirty();
        }

        private void FinishSwitchAnimationAndFire()
        {
            ThingWithComps incoming = animationIncomingWeapon;
            BeginAimHandoff(incoming, animationAimAngle);
            animationOutgoingWeapon = null;
            animationIncomingWeapon = null;
            stateStartTick = -1;
            stateDurationTicks = 0;
            MarkBackWeaponsDirty();

            Verb verb = PrimaryVerbFor(incoming);
            if (verb == null || !IsTargetStillValid(cycleTarget))
            {
                WeaponWheelDevLog.CastDecision(this, verb, "FinishSwitch: no primary verb or target no longer valid, ending cycle");
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
                WeaponWheelDevLog.CastDecision(this, verb, "FinishSwitch: TryStartCastOn returned FALSE for the next weapon, ending cycle");
                FinishCycleWithCooldown(verb, cycleTarget);
            }
            else if (combatState == WeaponWheelCombatState.Firing && verb.state == VerbState.Idle)
            {
                // TryStartCastOn 可以返回 true，但特殊 Verb 仍可能在第一发失败；此时没有 burst 完成回调可负责收尾。
                WeaponWheelDevLog.CastDecision(this, verb, "FinishSwitch: cast started but verb went idle immediately (special verb?), ending cycle");
                FinishCycleWithCooldown(verb, cycleTarget);
            }
            else
            {
                WeaponWheelDevLog.CastDecision(this, verb, "FinishSwitch: next weapon firing");
            }
        }

        private void FinishCycleWithCooldown(Verb lastVerb, LocalTargetInfo target)
        {
            animationOutgoingWeapon = null;
            animationIncomingWeapon = null;
            MarkBackWeaponsDirty();
            NormalizeToPrimarySlot();

            cycleActive = false;
            cycleTarget = LocalTargetInfo.Invalid;
            combatSourceJob = null;

            int cooldown = Mathf.Max(1, accumulatedCooldownTicks);
            cooldownUntilTick = CurrentTick + cooldown;
            stateStartTick = CurrentTick;
            stateDurationTicks = cooldown;
            combatState = WeaponWheelCombatState.CycleCooldown;
            if (Pawn?.stances != null)
            {
                Verb stanceVerb = lastVerb ?? PrimaryVerbFor(Pawn.equipment?.Primary);
                Pawn.stances.SetStance(new Stance_Cooldown(cooldown, target, stanceVerb));
            }
        }

        private void StopActiveCombatAndRestorePrimary()
        {
            if (cycleActive && accumulatedCooldownTicks > 0 && !HasAutoloadingSystem)
            {
                FinishCycleWithCooldown(PrimaryVerbFor(Pawn?.equipment?.Primary), cycleTarget);
                return;
            }
            AbortCombatAndRestorePrimary(true);
        }

        internal void AbortCombatAndRestorePrimary(bool cancelStance)
        {
            Pawn pawn = Pawn;
            if (cancelStance && pawn?.stances != null && pawn.stances.curStance is Stance_Busy busy
                && ContainsWeapon(busy.verb?.EquipmentSource))
            {
                busy.verb?.Reset();
                pawn.stances.SetStance(new Stance_Mobile());
            }

            cycleActive = false;
            cycleTarget = LocalTargetInfo.Invalid;
            combatSourceJob = null;
            accumulatedCooldownTicks = 0;
            cooldownUntilTick = -1;
            animationOutgoingWeapon = null;
            animationIncomingWeapon = null;
            animationSnapSoundPlayed = false;
            aimHandoffWeapon = null;
            aimHandoffUntilTick = -1;
            ClearPendingCast();
            ClearPendingSwordDance();
            NormalizeToPrimarySlot();
            combatState = wasWeaponOpenlyHeld ? WeaponWheelCombatState.Ready : WeaponWheelCombatState.Holstered;
            MarkBackWeaponsDirty();
        }

        private int FindNextFireableSlot(int startIndex, LocalTargetInfo target)
        {
            for (int i = Mathf.Max(0, startIndex); i < slots.Count; i++)
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
            if (slotIndex < 0 || slotIndex >= slots.Count || !unlockedSlots[slotIndex])
            {
                return false;
            }
            ThingWithComps weapon = slots[slotIndex];
            Verb verb = PrimaryVerbFor(weapon);
            return weapon != null && weapon.def.IsRangedWeapon && verb != null && verb.Available()
                && IsTargetStillValid(target) && verb.CanHitTarget(target);
        }

        private bool IsTargetStillValid(LocalTargetInfo target)
        {
            if (!target.IsValid)
            {
                return false;
            }
            if (!target.HasThing)
            {
                return Pawn?.Map != null && target.Cell.InBounds(Pawn.Map);
            }
            Thing thing = target.Thing;
            if (thing == null || thing.Destroyed || !thing.Spawned || thing.Map != Pawn?.Map)
            {
                return false;
            }
            return !(thing is Pawn targetPawn)
                || (!targetPawn.Dead && !targetPawn.Downed && !targetPawn.IsPsychologicallyInvisible());
        }

        private bool ShouldAbortActiveCombat()
        {
            if (combatSourceJob != null && Pawn?.CurJob != combatSourceJob)
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
            return combatSourceJob?.def == JobDefOf.Wait_Combat
                && Pawn?.drafter != null
                && !Pawn.drafter.FireAtWill;
        }

        private Verb PrimaryVerbFor(ThingWithComps weapon)
        {
            return weapon?.GetComp<CompEquippable>()?.PrimaryVerb;
        }

        private bool IsCurrentWheelVerb(Verb verb)
        {
            ThingWithComps equipment = verb?.EquipmentSource;
            return verb != null && equipment != null && equipment == Pawn?.equipment?.Primary && ContainsWeapon(equipment);
        }

        // 兼容会把轮盘备用武器注册进 Pawn 攻击 Verb 池的框架。
        // 普通攻击入口只允许返回真实主武器；轮射直接调用各槽位 Verb，不经过 Pawn.TryGetAttackVerb。
        internal bool TryCorrectAttackVerbSelection(
            Verb selectedVerb,
            bool allowManualCastWeapons,
            out Verb correctedVerb)
        {
            correctedVerb = selectedVerb;
            Pawn pawn = Pawn;
            ThingWithComps selectedEquipment = selectedVerb?.EquipmentSource;
            ThingWithComps primary = pawn?.equipment?.Primary;
            if (selectedEquipment == null || selectedEquipment == primary || !ContainsWeapon(selectedEquipment))
            {
                return false;
            }

            Verb primaryVerb = PrimaryVerbFor(primary);
            bool primaryUsable = primaryVerb != null
                && primaryVerb.verbProps != null
                && primaryVerb.Available()
                && (!primaryVerb.verbProps.onlyManualCast
                    || (pawn.CurJob != null && pawn.CurJob.def != JobDefOf.Wait_Combat)
                    || allowManualCastWeapons);
            correctedVerb = primaryUsable ? primaryVerb : null;
            return true;
        }

        private float AimAngleFor(LocalTargetInfo target)
        {
            Pawn pawn = Pawn;
            if (pawn == null)
            {
                return 0f;
            }
            if (target.IsValid)
            {
                Vector3 targetPos = target.HasThing ? target.Thing.DrawPos : target.Cell.ToVector3Shifted();
                Vector3 difference = targetPos - pawn.DrawPos;
                if (difference.MagnitudeHorizontalSquared() > 0.001f)
                {
                    return difference.AngleFlat();
                }
            }
            return pawn.Rotation.AsAngle;
        }

        private void RememberPendingCast(
            LocalTargetInfo castTarget,
            LocalTargetInfo destinationTarget,
            bool surpriseAttack,
            bool canHitNonTargetPawns,
            bool preventFriendlyFire,
            bool nonInterruptingSelfCast)
        {
            combatSourceJob = Pawn?.CurJob;
            pendingCastTarget = castTarget;
            pendingCastDestination = destinationTarget;
            pendingSurpriseAttack = surpriseAttack;
            pendingCanHitNonTargetPawns = canHitNonTargetPawns;
            pendingPreventFriendlyFire = preventFriendlyFire;
            pendingNonInterruptingSelfCast = nonInterruptingSelfCast;
        }

        private void ClearPendingCast()
        {
            pendingCastTarget = LocalTargetInfo.Invalid;
            pendingCastDestination = LocalTargetInfo.Invalid;
            pendingSurpriseAttack = false;
            pendingCanHitNonTargetPawns = true;
            pendingPreventFriendlyFire = false;
            pendingNonInterruptingSelfCast = false;
        }

        private void BeginAimHandoff(ThingWithComps weapon, float aimAngle)
        {
            aimHandoffWeapon = weapon;
            aimHandoffAngle = aimAngle;
            aimHandoffUntilTick = CurrentTick + 1;
        }

        // 以下两个方法仅供 WeaponWheelDevLog 使用：汇总私有战斗状态，未开启开发者日志时不会被调用。
        internal string DevDescribeCombat()
        {
            Pawn pawn = Pawn;
            return "state=" + combatState
                + " activeSlot=" + activeSlotIndex
                + " cycleActive=" + cycleActive
                + " cycleTarget=" + (cycleTarget.IsValid ? cycleTarget.ToString() : "none")
                + " eligible=" + IsFullFirepowerEligible()
                + " autoloading=" + HasAutoloadingSystem
                + " openlyHeld=" + wasWeaponOpenlyHeld
                + " job=" + (pawn?.CurJob?.def?.defName ?? "none")
                + " fireAtWill=" + (pawn?.drafter == null ? "n/a" : pawn.drafter.FireAtWill.ToString())
                + " primary=" + (pawn?.equipment?.Primary?.def?.defName ?? "none");
        }

        private string DevAbortReason()
        {
            Pawn pawn = Pawn;
            if (combatSourceJob != null && pawn?.CurJob != combatSourceJob)
            {
                return "job changed (was " + (combatSourceJob.def?.defName ?? "unknown") + ", now "
                    + (pawn?.CurJob?.def?.defName ?? "none") + "; another mod interrupting jobs breaks the cycle)";
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

        private int CurrentAnimationSnapTick => Mathf.Clamp(
            combatState == WeaponWheelCombatState.Switching || combatState == WeaponWheelCombatState.SwordDanceSwitching
                ? Props.switchSnapTick
                : Props.snapTick,
            0,
            stateDurationTicks);
    }
}
