using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl.Features.WeaponWheel
{
    public sealed partial class Comp_WeaponWheel
    {
        private const int SwordDanceGuaranteeFailures = 5;

        private Verb pendingSwordDanceVerb;
        private Pawn pendingSwordDanceTarget;
        private int pendingSwordDanceNextSlot = -1;
        private bool pendingSwordDanceStrikeResolved;
        private bool pendingSwordDanceStrikeDelayed;
        private bool resolvingDelayedSwordDanceStrike;
        private int swordDanceFailureStreak;
        private const float SwordDanceDashPauseEnd = 5f / 42f;
        private const float SwordDanceDashRecoilEnd = 15f / 42f;
        private const float SwordDanceDashRushEnd = 24f / 42f;
        private const float SwordDanceDashOvershootEnd = 31f / 42f;
        private const float SwordDanceDashBufferDistance = 0.5f;

        private Vector3 swordDanceDashOrigin;
        private Vector3 swordDanceDashDestination;
        private Vector3 swordDanceDashDirection;
        private Rot4 swordDanceDashForwardRotation = Rot4.Invalid;
        private Rot4 swordDanceDashReturnRotation = Rot4.Invalid;
        private int swordDanceDashStartTick = -1;
        private int swordDanceDashDurationTicks;
        private float swordDanceDashImpactProgress = 0.5f;

        public bool IsSwordDanceEligible()
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
                Verb verb = PrimaryVerbFor(weapon);
                if (!weapon.def.IsMeleeWeapon || verb?.verbProps?.IsMeleeAttack != true)
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

        internal bool TryDelaySwordDanceStrikeUntilImpact(Verb verb)
        {
            if (resolvingDelayedSwordDanceStrike)
            {
                return false;
            }
            if (IsSwordDanceStrikeAwaitingImpact(verb))
            {
                return true;
            }

            ClearPendingSwordDance();
            if (verb?.verbProps?.IsMeleeAttack != true
                || !IsSwordDanceAttack(verb)
                || !IsSwordDanceEligible())
            {
                return false;
            }

            Pawn pawn = Pawn;
            Pawn target = verb.CurrentTarget.Pawn;
            int nextSlot = FindNextMeleeSlot(activeSlotIndex);
            if (pawn == null || target == null || target == pawn || target.Dead || target.Destroyed
                || pawn.stances?.FullBodyBusy == true
                || !target.Spawned || target.Map != pawn.Map || !pawn.HostileTo(target)
                || nextSlot < 0 || !TryFindSwordDanceDestination(target, out IntVec3 destination)
                || !RollSwordDanceChance())
            {
                return false;
            }

            pendingSwordDanceVerb = verb;
            pendingSwordDanceTarget = target;
            pendingSwordDanceNextSlot = nextSlot;
            pendingSwordDanceStrikeResolved = false;
            pendingSwordDanceStrikeDelayed = true;

            Map map = pawn.Map;
            IntVec3 origin = pawn.Position;
            bool dashStarted = false;
            if (origin != destination)
            {
                FleckMaker.ThrowDustPuff(origin, map, 1.15f);
                BeginSwordDanceDashAnimation(destination, target.Position.ToVector3Shifted());
                pawn.Position = destination;
                // 逻辑位置立即完成换位，绘制位置与朝向由剑舞自己的分段冲刺动画接管。
                pawn.Notify_Teleported(endCurrentJob: false, resetTweenedPos: false);
                dashStarted = true;
            }
            if (!dashStarted)
            {
                pawn.rotationTracker?.Face(target.DrawPos);
            }
            target.stances?.stunner?.StunFor(
                Mathf.Max(1, Props.swordDanceStunTicks),
                pawn,
                addBattleLog: false);
            ApplySwordDanceHaste(pawn);
            return dashStarted;
        }

        internal bool IsSwordDanceStrikeAwaitingImpact(Verb verb)
        {
            return pendingSwordDanceStrikeDelayed
                && !pendingSwordDanceStrikeResolved
                && pendingSwordDanceVerb == verb;
        }

        private void BeginSwordDanceDashAnimation(IntVec3 destination, Vector3 targetPosition)
        {
            Pawn pawn = Pawn;
            Vector3 origin = CurrentSwordDanceDashRootPosition();
            origin.y = 0f;
            Vector3 end = destination.ToVector3Shifted();
            end.y = 0f;
            Vector3 direction = end - origin;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;

            swordDanceDashOrigin = origin;
            swordDanceDashDestination = end;
            swordDanceDashDirection = direction;
            swordDanceDashForwardRotation = Rot4.FromAngleFlat(direction.AngleFlat());
            swordDanceDashReturnRotation = swordDanceDashForwardRotation.Opposite;
            swordDanceDashStartTick = CurrentTick;
            swordDanceDashDurationTicks = Mathf.Max(1, Props.swordDanceDashAnimationTicks);
            Vector3 recoil = origin - direction * SwordDanceDashBufferDistance;
            Vector3 rush = end - recoil;
            float targetFraction = rush.sqrMagnitude <= 0.0001f
                ? 0.5f
                : Mathf.Clamp01(Vector3.Dot(targetPosition - recoil, rush) / rush.sqrMagnitude);
            swordDanceDashImpactProgress = Mathf.Lerp(
                SwordDanceDashRecoilEnd,
                SwordDanceDashRushEnd,
                Mathf.Sqrt(targetFraction));
            if (pawn != null)
            {
                pawn.Rotation = swordDanceDashForwardRotation;
            }
        }

        internal bool TryGetSwordDanceDashDrawPosition(Vector3 vanillaDrawPosition, out Vector3 drawPosition)
        {
            drawPosition = vanillaDrawPosition;
            Pawn pawn = Pawn;
            if (swordDanceDashStartTick < 0 || pawn?.Drawer?.tweener == null)
            {
                return false;
            }

            Vector3 dashRoot = CurrentSwordDanceDashRootPosition();
            Vector3 vanillaRoot = pawn.Drawer.tweener.TweenedPos;
            drawPosition = dashRoot + (vanillaDrawPosition - vanillaRoot);
            return true;
        }

        private Vector3 CurrentSwordDanceDashRootPosition()
        {
            if (swordDanceDashStartTick < 0)
            {
                Pawn pawn = Pawn;
                return pawn?.Drawer?.tweener?.TweenedPos ?? pawn?.Position.ToVector3Shifted() ?? Vector3.zero;
            }

            int duration = Mathf.Max(1, swordDanceDashDurationTicks);
            float progress = Mathf.Clamp01((float)(CurrentTick - swordDanceDashStartTick) / duration);
            Vector3 recoil = swordDanceDashOrigin - swordDanceDashDirection * SwordDanceDashBufferDistance;
            Vector3 overshoot = swordDanceDashDestination + swordDanceDashDirection * SwordDanceDashBufferDistance;

            if (progress < SwordDanceDashPauseEnd)
            {
                return swordDanceDashOrigin;
            }
            if (progress < SwordDanceDashRecoilEnd)
            {
                float phase = Mathf.InverseLerp(SwordDanceDashPauseEnd, SwordDanceDashRecoilEnd, progress);
                return Vector3.LerpUnclamped(swordDanceDashOrigin, recoil, SmoothStep01(phase));
            }
            if (progress < SwordDanceDashRushEnd)
            {
                float phase = Mathf.InverseLerp(SwordDanceDashRecoilEnd, SwordDanceDashRushEnd, progress);
                return Vector3.LerpUnclamped(recoil, swordDanceDashDestination, phase * phase);
            }
            if (progress < SwordDanceDashOvershootEnd)
            {
                float phase = Mathf.InverseLerp(SwordDanceDashRushEnd, SwordDanceDashOvershootEnd, progress);
                float decelerating = 1f - (1f - phase) * (1f - phase);
                return Vector3.LerpUnclamped(swordDanceDashDestination, overshoot, decelerating);
            }

            float returnPhase = Mathf.InverseLerp(SwordDanceDashOvershootEnd, 1f, progress);
            return Vector3.LerpUnclamped(overshoot, swordDanceDashDestination, SmoothStep01(returnPhase));
        }

        private static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        internal bool MaintainSwordDanceDashFacing()
        {
            Pawn pawn = Pawn;
            if (swordDanceDashStartTick < 0 || pawn == null)
            {
                return false;
            }

            int duration = Mathf.Max(1, swordDanceDashDurationTicks);
            float progress = Mathf.Clamp01((float)(CurrentTick - swordDanceDashStartTick) / duration);
            pawn.Rotation = progress < SwordDanceDashOvershootEnd
                ? swordDanceDashForwardRotation
                : swordDanceDashReturnRotation;
            return true;
        }

        internal float SwordDanceDashAimAngle(float fallbackAngle)
        {
            if (swordDanceDashStartTick < 0 || swordDanceDashDirection.sqrMagnitude <= 0.0001f)
            {
                return fallbackAngle;
            }

            int duration = Mathf.Max(1, swordDanceDashDurationTicks);
            float progress = Mathf.Clamp01((float)(CurrentTick - swordDanceDashStartTick) / duration);
            Vector3 facingDirection = progress < SwordDanceDashOvershootEnd
                ? swordDanceDashDirection
                : -swordDanceDashDirection;
            return facingDirection.AngleFlat();
        }

        internal bool TryGetSwordDanceDashHeldWeapon(out ThingWithComps weapon, out float aimAngle)
        {
            weapon = Pawn?.equipment?.Primary;
            aimAngle = SwordDanceDashAimAngle(0f);
            return weapon != null
                && pendingSwordDanceStrikeDelayed
                && !pendingSwordDanceStrikeResolved
                && swordDanceDashStartTick >= 0;
        }

        private void TickSwordDanceDashAnimation()
        {
            if (swordDanceDashStartTick < 0)
            {
                return;
            }

            int duration = Mathf.Max(1, swordDanceDashDurationTicks);
            int elapsed = CurrentTick - swordDanceDashStartTick;
            float progress = Mathf.Clamp01((float)elapsed / duration);
            if (pendingSwordDanceStrikeDelayed
                && !pendingSwordDanceStrikeResolved
                && progress >= swordDanceDashImpactProgress)
            {
                ResolveDelayedSwordDanceStrike();
            }
            if (elapsed < duration)
            {
                return;
            }

            if (pendingSwordDanceStrikeDelayed && !pendingSwordDanceStrikeResolved)
            {
                ResolveDelayedSwordDanceStrike();
            }
            Pawn pawn = Pawn;
            if (pawn?.Spawned == true)
            {
                FleckMaker.ThrowDustPuff(swordDanceDashDestination, pawn.Map, 1.35f);
                pawn.Drawer?.tweener?.ResetTweenedPosToRoot();
                pawn.Rotation = swordDanceDashReturnRotation;
            }
            ClearSwordDanceDashAnimation();
        }

        private void ResolveDelayedSwordDanceStrike()
        {
            Verb verb = pendingSwordDanceVerb;
            if (verb == null || resolvingDelayedSwordDanceStrike)
            {
                return;
            }

            resolvingDelayedSwordDanceStrike = true;
            bool invoked;
            try
            {
                invoked = Harmony_WeaponWheel_BurstCompleted.TryInvokeDelayedMeleeBurst(verb);
            }
            finally
            {
                resolvingDelayedSwordDanceStrike = false;
                MaintainSwordDanceDashFacing();
            }

            if (!invoked)
            {
                verb.Reset();
                ClearPendingSwordDance();
            }
        }

        private void ClearSwordDanceDashAnimation()
        {
            swordDanceDashOrigin = Vector3.zero;
            swordDanceDashDestination = Vector3.zero;
            swordDanceDashDirection = Vector3.zero;
            swordDanceDashForwardRotation = Rot4.Invalid;
            swordDanceDashReturnRotation = Rot4.Invalid;
            swordDanceDashStartTick = -1;
            swordDanceDashDurationTicks = 0;
            swordDanceDashImpactProgress = 0.5f;
        }

        private bool RollSwordDanceChance()
        {
            if (Rand.Chance(Mathf.Clamp01(Props.swordDanceChance)))
            {
                swordDanceFailureStreak = 0;
                return true;
            }

            swordDanceFailureStreak++;
            if (swordDanceFailureStreak >= SwordDanceGuaranteeFailures)
            {
                swordDanceFailureStreak = 0;
                return true;
            }
            return false;
        }

        internal void NotifySwordDanceStrikeResolved(Verb verb)
        {
            if (pendingSwordDanceVerb == verb && pendingSwordDanceNextSlot >= 0)
            {
                pendingSwordDanceStrikeResolved = true;
            }
        }

        internal bool NotifyMeleeAttackCompleted(Verb verb)
        {
            if (!IsSwordDanceAttack(verb))
            {
                return false;
            }

            bool triggered = pendingSwordDanceVerb == verb
                && pendingSwordDanceNextSlot >= 0
                && pendingSwordDanceStrikeResolved;
            Pawn target = pendingSwordDanceTarget;
            int nextSlot = pendingSwordDanceNextSlot;
            ClearPendingSwordDance();
            combatSourceJob = null;

            if (!triggered)
            {
                return false;
            }

            ApplySwordDanceRamDamage(target, verb);
            BeginSwordDanceSwitchTo(nextSlot, target, verb);
            return true;
        }

        private void TickPendingSwordDanceCompletion()
        {
            if (pendingSwordDanceStrikeResolved && pendingSwordDanceVerb != null)
            {
                NotifyMeleeAttackCompleted(pendingSwordDanceVerb);
            }
        }

        private bool IsSwordDanceAttack(Verb verb)
        {
            Pawn pawn = Pawn;
            ThingWithComps primary = pawn?.equipment?.Primary;
            ThingWithComps attackEquipment = verb?.EquipmentSource;
            return verb?.verbProps?.IsMeleeAttack == true
                && verb.CasterPawn == pawn
                && primary?.def?.IsMeleeWeapon == true
                && ContainsWeapon(primary)
                // Horns, fists and other body tools have no equipment source. They still count as
                // the pawn's melee attacks while a weapon-wheel melee weapon is actively equipped.
                && (attackEquipment == null || attackEquipment == primary);
        }

        internal bool ShouldSuppressExternalMeleeAttackAnimation()
        {
            return pendingSwordDanceVerb != null
                && pendingSwordDanceNextSlot >= 0
                && !pendingSwordDanceStrikeResolved;
        }

        private int FindNextMeleeSlot(int currentSlot)
        {
            for (int offset = 1; offset < slots.Count; offset++)
            {
                int index = (currentSlot + offset) % slots.Count;
                ThingWithComps weapon = slots[index];
                if (unlockedSlots[index] && weapon?.def?.IsMeleeWeapon == true
                    && PrimaryVerbFor(weapon)?.verbProps?.IsMeleeAttack == true)
                {
                    return index;
                }
            }
            return -1;
        }

        private bool TryFindSwordDanceDestination(Pawn target, out IntVec3 destination)
        {
            destination = IntVec3.Invalid;
            Pawn pawn = Pawn;
            Map map = pawn?.Map;
            if (pawn == null || target == null || map == null)
            {
                return false;
            }

            IntVec3 relative = target.Position - pawn.Position;
            IntVec3 behindDirection = new IntVec3(
                relative.x == 0 ? 0 : (relative.x > 0 ? 1 : -1),
                0,
                relative.z == 0 ? 0 : (relative.z > 0 ? 1 : -1));
            if (behindDirection == IntVec3.Zero)
            {
                return false;
            }
            IntVec3 exactBehind = target.Position + behindDirection;
            if (IsValidSwordDanceDestination(exactBehind, pawn, map))
            {
                destination = exactBehind;
                return true;
            }

            int bestScore = int.MinValue;
            foreach (IntVec3 cell in GenAdj.CellsAdjacent8Way(target))
            {
                IntVec3 offset = cell - target.Position;
                int behindScore = offset.x * behindDirection.x + offset.z * behindDirection.z;
                if (behindScore <= 0 || !IsValidSwordDanceDestination(cell, pawn, map))
                {
                    continue;
                }

                int score = behindScore * 10 - cell.DistanceToSquared(exactBehind);
                if (score > bestScore)
                {
                    bestScore = score;
                    destination = cell;
                }
            }
            return destination.IsValid;
        }

        private static bool IsValidSwordDanceDestination(IntVec3 cell, Pawn pawn, Map map)
        {
            if (!cell.InBounds(map) || !cell.Standable(map))
            {
                return false;
            }
            Pawn occupyingPawn = cell.GetFirstPawn(map);
            if (occupyingPawn != null && occupyingPawn != pawn)
            {
                return false;
            }
            return cell == pawn.Position
                || GenPlace.HaulPlaceBlockerIn(pawn, cell, map, checkBlueprintsAndFrames: false) == null;
        }

        private void ApplySwordDanceRamDamage(Pawn target, Verb meleeVerb)
        {
            Pawn pawn = Pawn;
            if (pawn == null || target == null || target.Destroyed || !target.Spawned || target.Map != pawn.Map)
            {
                return;
            }

            ThingWithComps weapon = meleeVerb?.EquipmentSource ?? pawn.equipment?.Primary;
            DamageInfo damage = new DamageInfo(
                DamageDefOf.Blunt,
                Mathf.Max(0f, Props.swordDanceRamDamage),
                Mathf.Max(0f, Props.swordDanceRamArmorPenetration),
                (target.Position - pawn.Position).AngleFlat,
                pawn,
                null,
                weapon?.def,
                DamageInfo.SourceCategory.ThingOrUnknown,
                target);
            target.TakeDamage(damage);
        }

        private void ApplySwordDanceHaste(Pawn pawn)
        {
            if (pawn?.health == null || Mugirl_DefOf.Mugirl_LinkedSwordDance == null)
            {
                return;
            }

            Hediff haste = pawn.health.hediffSet.GetFirstHediffOfDef(Mugirl_DefOf.Mugirl_LinkedSwordDance);
            if (haste == null)
            {
                haste = pawn.health.AddHediff(Mugirl_DefOf.Mugirl_LinkedSwordDance);
            }
            haste?.TryGetComp<HediffComp_Disappears>()?.SetDuration(Mathf.Max(1, Props.swordDanceHasteTicks));
        }

        private void BeginSwordDanceSwitchTo(int slotIndex, LocalTargetInfo target, Verb completedVerb)
        {
            aimHandoffWeapon = null;
            aimHandoffUntilTick = -1;
            ThingWithComps outgoing = Pawn?.equipment?.Primary;
            ThingWithComps incoming = WeaponAt(slotIndex);
            if (incoming == null || !PromoteSlotToPrimary(slotIndex))
            {
                combatState = wasWeaponOpenlyHeld ? WeaponWheelCombatState.Ready : WeaponWheelCombatState.Holstered;
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
            combatState = WeaponWheelCombatState.SwordDanceSwitching;
            Verb incomingVerb = PrimaryVerbFor(incoming);
            if (Pawn?.stances != null && incomingVerb != null)
            {
                Pawn.stances.SetStance(new Stance_Cooldown(stateDurationTicks, target, incomingVerb));
            }
            MarkBackWeaponsDirty();
        }

        private bool PromoteSlotToPrimary(int slotIndex)
        {
            EnsureCollections();
            if (slotIndex <= 0 || slotIndex >= slots.Count || slots[slotIndex] == null)
            {
                return false;
            }

            List<int> occupiedIndices = new List<int>();
            for (int i = 0; i < slots.Count; i++)
            {
                if (unlockedSlots[i] && slots[i] != null)
                {
                    occupiedIndices.Add(i);
                }
            }

            int promotedPosition = occupiedIndices.IndexOf(slotIndex);
            if (occupiedIndices.Count < 2 || promotedPosition < 0 || occupiedIndices[0] != 0)
            {
                return false;
            }

            List<ThingWithComps> rotatedWeapons = new List<ThingWithComps>(occupiedIndices.Count);
            for (int offset = 0; offset < occupiedIndices.Count; offset++)
            {
                int sourcePosition = (promotedPosition + offset) % occupiedIndices.Count;
                rotatedWeapons.Add(slots[occupiedIndices[sourcePosition]]);
            }

            if (!MoveActiveWeaponTo(slotIndex))
            {
                return false;
            }

            for (int i = 0; i < occupiedIndices.Count; i++)
            {
                slots[occupiedIndices[i]] = rotatedWeapons[i];
            }
            activeSlotIndex = 0;
            MarkBackWeaponsDirty();
            return Pawn?.equipment?.Primary == slots[0];
        }

        private void FinishSwordDanceSwitchAnimation()
        {
            ThingWithComps incoming = animationIncomingWeapon;
            BeginAimHandoff(incoming, animationAimAngle);
            animationOutgoingWeapon = null;
            animationIncomingWeapon = null;
            stateStartTick = -1;
            stateDurationTicks = 0;
            combatState = wasWeaponOpenlyHeld ? WeaponWheelCombatState.Ready : WeaponWheelCombatState.Holstered;
            MarkBackWeaponsDirty();
        }

        private void ClearPendingSwordDance()
        {
            pendingSwordDanceVerb = null;
            pendingSwordDanceTarget = null;
            pendingSwordDanceNextSlot = -1;
            pendingSwordDanceStrikeResolved = false;
            pendingSwordDanceStrikeDelayed = false;
        }
    }
}
