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
                if (!IsFullFirepowerEligible(out _))
                {
                    return "Mugirl.WeaponWheel.Tab".Translate();
                }
                return HasAutoloadingSystem
                    ? "Mugirl.WeaponWheel.StatusFullAutoload".Translate()
                    : "Mugirl.WeaponWheel.StatusFull".Translate();
            }
        }

        public bool IsFullFirepowerEligible(out string reason)
        {
            reason = null;
            EnsureCollections();
            Pawn pawn = Pawn;
            if (pawn == null || pawn.equipment == null)
            {
                reason = "Mugirl.WeaponWheel.ReasonNoEquipment".Translate();
                return false;
            }
            if (IsCombatDisabledByMount())
            {
                reason = "Mugirl.WeaponWheel.ReasonMounted".Translate();
                return false;
            }
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                reason = "Mugirl.WeaponWheel.ReasonViolenceDisabled".Translate();
                return false;
            }
            if (slots[0] == null)
            {
                reason = "Mugirl.WeaponWheel.ReasonNoPrimary".Translate();
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
                    reason = "Mugirl.WeaponWheel.ReasonNotRanged".Translate(weapon.LabelCap);
                    return false;
                }
            }
            if (weaponCount < 2)
            {
                reason = "Mugirl.WeaponWheel.ReasonNeedTwo".Translate();
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
            if (!IsCurrentWheelVerb(verb) || WeaponWheelWarmupScope.IsSkipping(verb) || IsCombatDisabledByMount())
            {
                return false;
            }

            if (combatState == WeaponWheelCombatState.CycleCooldown && CurrentTick < cooldownUntilTick)
            {
                return true;
            }
            if (combatState == WeaponWheelCombatState.Switching)
            {
                return true;
            }
            if (combatState == WeaponWheelCombatState.EquippingPrimary)
            {
                RememberPendingCast(castTarget, destinationTarget, surpriseAttack, canHitNonTargetPawns, preventFriendlyFire, nonInterruptingSelfCast);
                return true;
            }

            bool openlyHeld = PawnRenderUtility.CarryWeaponOpenly(Pawn);
            if (!wasWeaponOpenlyHeld || !openlyHeld || combatState == WeaponWheelCombatState.Holstered)
            {
                wasWeaponOpenlyHeld = true;
                RememberPendingCast(castTarget, destinationTarget, surpriseAttack, canHitNonTargetPawns, preventFriendlyFire, nonInterruptingSelfCast);
                BeginPrimaryEquipAnimation(castTarget);
                return true;
            }

            return false;
        }

        internal void NotifyCastStarted(Verb verb, bool started)
        {
            if (!started || !IsCurrentWheelVerb(verb) || IsCombatDisabledByMount())
            {
                return;
            }
            combatState = WeaponWheelCombatState.Firing;
        }

        internal void NotifyBurstCompleted(Verb verb)
        {
            if (!IsCurrentWheelVerb(verb))
            {
                return;
            }

            if (!IsFullFirepowerEligible(out _))
            {
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
                    FinishCycleWithCooldown(verb, target);
                    return;
                }
                cycleActive = true;
                cycleTarget = target;
                combatSourceJob = Pawn?.CurJob;
                accumulatedCooldownTicks = 0;
            }

            accumulatedCooldownTicks += Mathf.Max(0, verb.verbProps.AdjustedCooldownTicks(verb, Pawn));
            int nextSlot = FindNextFireableSlot(activeSlotIndex + 1, cycleTarget);
            if (nextSlot >= 0)
            {
                BeginSwitchTo(nextSlot, cycleTarget, verb);
                return;
            }

            if (HasAutoloadingSystem && IsTargetStillValid(cycleTarget) && CanSlotFireAt(0, cycleTarget))
            {
                accumulatedCooldownTicks = 0;
                BeginSwitchTo(0, cycleTarget, verb);
                return;
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
                || (weapon == aimHandoffWeapon && CurrentTick <= aimHandoffUntilTick);
        }

        internal bool IsCombatDisabledByMount()
        {
            Pawn pawn = Pawn;
            if (pawn == null)
            {
                return false;
            }
            Comp_MugirlMount carrierComp = pawn.TryGetComp<Comp_MugirlMount>();
            return carrierComp?.HasMountedPawn == true || MountedPawnUtility.IsMounted(pawn, out _);
        }

        internal void MaintainCombatFacing()
        {
            Pawn pawn = Pawn;
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
                StopActiveCombatAndRestorePrimary();
                return;
            }

            int elapsed = stateStartTick < 0 ? 0 : CurrentTick - stateStartTick;
            if ((combatState == WeaponWheelCombatState.EquippingPrimary || combatState == WeaponWheelCombatState.Switching)
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
                FinishCycleWithCooldown(verb, cycleTarget);
            }
            else if (combatState == WeaponWheelCombatState.Firing && verb.state == VerbState.Idle)
            {
                // TryStartCastOn 可以返回 true，但特殊 Verb 仍可能在第一发失败；此时没有 burst 完成回调可负责收尾。
                FinishCycleWithCooldown(verb, cycleTarget);
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

        private int CurrentAnimationSnapTick => Mathf.Clamp(
            combatState == WeaponWheelCombatState.Switching ? Props.switchSnapTick : Props.snapTick,
            0,
            stateDurationTicks);
    }
}
