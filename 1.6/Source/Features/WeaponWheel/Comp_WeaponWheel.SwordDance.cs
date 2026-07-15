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
        private int swordDanceFailureStreak;

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

        internal void PrepareSwordDanceStrike(Verb verb)
        {
            ClearPendingSwordDance();
            if (verb?.verbProps?.IsMeleeAttack != true
                || !IsSwordDanceAttack(verb)
                || !IsSwordDanceEligible())
            {
                return;
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
                return;
            }

            pendingSwordDanceVerb = verb;
            pendingSwordDanceTarget = target;
            pendingSwordDanceNextSlot = nextSlot;
            pendingSwordDanceStrikeResolved = false;

            Map map = pawn.Map;
            IntVec3 origin = pawn.Position;
            if (origin != destination)
            {
                FleckMaker.ThrowDustPuff(origin, map, 1.15f);
                pawn.Position = destination;
                // 保留绘制插值，让角色从原位置穿过目标移动到对侧；只重置寻路器，不重置 PawnTweener。
                pawn.Notify_Teleported(endCurrentJob: false, resetTweenedPos: false);
                FleckMaker.ThrowDustPuff(destination, map, 1.35f);
            }
            pawn.rotationTracker?.Face(target.DrawPos);
            target.stances?.stunner?.StunFor(
                Mathf.Max(1, Props.swordDanceStunTicks),
                pawn,
                addBattleLog: false);
            ApplySwordDanceHaste(pawn);
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
            return verb?.verbProps?.IsMeleeAttack == true
                && verb.CasterPawn == pawn
                && verb.EquipmentSource == primary
                && primary?.def?.IsMeleeWeapon == true
                && ContainsWeapon(primary);
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
        }
    }
}
