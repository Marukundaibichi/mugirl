using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public static partial class MountedCombatController
    {
        private const int DefaultTurretTickInterval = 60;

        public static void NotifyMounted(Comp_MugirlMount comp)
        {
            Verb verb = GetPrimaryRangedVerb(comp);
            if (verb != null)
            {
                EnsureVerbCaster(comp, verb);
            }
        }

        public static void NotifyDismounting(Comp_MugirlMount comp)
        {
            Pawn rider = comp?.MountedPawn;
            Verb verb = GetPrimaryRangedVerb(rider);
            CancelMountedCast(comp, verb);
            RestoreVerbCaster(rider);
            if (comp != null)
            {
                comp.turretBurstCooldownTicksLeft = 0;
                ClearAim(comp);
                comp.turretLastAttackedTarget = LocalTargetInfo.Invalid;
            }
        }

        public static void NotifyDismounting(Comp_MugirlMount comp, Pawn rider)
        {
            Verb verb = GetPrimaryRangedVerb(rider);
            CancelMountedCast(comp, verb);
            RestoreVerbCaster(rider);
            if (comp != null)
            {
                comp.turretBurstCooldownTicksLeft = 0;
                ClearAim(comp);
                comp.turretLastAttackedTarget = LocalTargetInfo.Invalid;
            }
        }

        public static void NotifyEquipmentRemoved(ThingWithComps equipment)
        {
            CompEquippable comp = equipment?.GetComp<CompEquippable>();
            List<Verb> verbs = comp?.AllVerbs;
            if (verbs == null)
            {
                return;
            }

            for (int i = 0; i < verbs.Count; i++)
            {
                ForgetVerb(verbs[i]);
            }
        }

        public static void VerbTick(Comp_MugirlMount comp, int delta)
        {
            Verb verb = GetPrimaryRangedVerb(comp);
            if (verb == null)
            {
                return;
            }

            EnsureVerbCaster(comp, verb);
            Pawn carrier = comp.MooPawn;
            int ticks = Mathf.Max(1, delta);
            for (int i = 0; i < ticks; i++)
            {
                Stance originalStance = carrier?.stances?.curStance;
                using (MountedCasterScope(comp, verb))
                {
                    verb.VerbTick();
                    RestoreCarrierStanceIfMountedVerbChangedIt(carrier, verb, originalStance);
                }
            }
        }

        public static void Tick(Comp_MugirlMount comp, int delta)
        {
            if (comp?.turretAimTarget.IsValid == true)
            {
                return;
            }

            if (!CanUseMountedRangedWeapon(comp, out _))
            {
                CancelMountedCast(comp, GetPrimaryRangedVerb(comp));
                ClearAim(comp);
                return;
            }

            Pawn carrier = comp.MooPawn;
            Pawn rider = comp.MountedPawn;
            Verb verb = GetPrimaryRangedVerb(comp);
            if (carrier == null || verb == null)
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

            if (comp.turretBurstCooldownTicksLeft > 0)
            {
                comp.turretBurstCooldownTicksLeft = Mathf.Max(0, comp.turretBurstCooldownTicksLeft - delta);
                return;
            }

            Thing target = FindBestTarget(comp, verb);
            if (target == null)
            {
                return;
            }

            comp.turretAimTarget = target;
            comp.turretAimTicksTotal = GetMountedAimTicks(comp, rider, verb);
            comp.turretAimTicksLeft = comp.turretAimTicksTotal;
            comp.turretCastStartTick = -1;
            if (comp.turretAimTicksLeft <= 0 && !TryStartMountedCast(comp, verb, comp.turretAimTarget))
            {
                comp.turretBurstCooldownTicksLeft = TurretTickInterval(comp);
            }
        }

        public static void TickAim(Comp_MugirlMount comp, int delta)
        {
            if (comp == null)
            {
                return;
            }

            Pawn carrier = comp.MooPawn;
            Verb verb = GetPrimaryRangedVerb(comp);
            // CompTickInterval 已先推进 VerbTick。完全空闲时跳过健康、能力与 caster 的重复检查；
            // 仍有目标、施放、暖机或骑乘 busy stance 时必须走原来的取消和收尾路径。
            if (!comp.turretAimTarget.IsValid && !WasMountedCastStarted(comp)
                && comp.turretAimTicksLeft == 0 && comp.turretAimTicksTotal == 0
                && (verb == null || (verb.state == VerbState.Idle
                    && !verb.CurrentTarget.IsValid && !verb.CurrentDestination.IsValid
                    && verb.castCompleteCallback == null))
                && !(carrier?.stances?.curStance is Stance_Busy busyStance
                    && (busyStance.verb == verb || (verb == null && IsMountedVerb(busyStance.verb)))))
            {
                return;
            }

            if (!CanUseMountedRangedWeapon(comp, out _))
            {
                CancelMountedCast(comp, verb);
                ClearAim(comp);
                return;
            }

            Pawn rider = comp.MountedPawn;
            if (carrier == null || verb == null)
            {
                ClearAim(comp);
                return;
            }

            EnsureVerbCaster(comp, verb);
            ClearCarrierMountedStance(carrier, verb);
            if (!comp.turretAimTarget.IsValid)
            {
                return;
            }

            if (verb.state == VerbState.Bursting)
            {
                return;
            }

            if (WasMountedCastStarted(comp))
            {
                FinishMountedCast(comp, verb, comp.turretAimTarget);
                return;
            }

            bool pointBlankMeleeTarget = comp.turretAimTarget.HasThing && comp.turretAimTarget.Thing == MountedPawnMeleeSupport.CurrentMeleeTarget(carrier);
            if (!IsValidTarget(carrier, verb, comp.turretAimTarget.Thing, allowPointBlank: pointBlankMeleeTarget))
            {
                CancelMountedCast(comp, verb);
                ClearAim(comp);
                return;
            }

            comp.turretAimTicksLeft = Mathf.Max(0, comp.turretAimTicksLeft - Mathf.Max(1, delta));
            if (comp.turretAimTicksLeft > 0)
            {
                return;
            }

            if (!TryStartMountedCast(comp, verb, comp.turretAimTarget))
            {
                comp.turretBurstCooldownTicksLeft = TurretTickInterval(comp);
            }
        }

        private static bool TryStartMountedCast(Comp_MugirlMount comp, Verb verb, LocalTargetInfo castTarget)
        {
            if (comp == null || verb == null || !castTarget.IsValid)
            {
                return false;
            }

            bool started;
            Pawn carrier = comp.MooPawn;
            if (carrier?.Map == null)
            {
                ClearAim(comp);
                return false;
            }

            Stance originalStance = carrier?.stances?.curStance;
            bool pointBlankMeleeTarget = castTarget.HasThing && castTarget.Thing == MountedPawnMeleeSupport.CurrentMeleeTarget(carrier);
            comp.turretCastStartTick = MugirlTickUtility.CurrentGameTickOrFallback(comp.turretCastStartTick);
            using (MountedCasterScope(comp, verb))
            using (new MountedWarmupOverride(verb, 0f))
            using (new MountedMinRangeOverride(verb, pointBlankMeleeTarget ? 0f : (float?)null))
            {
                carrier?.stances?.SetStance(new Stance_Mobile());
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

        private static void FinishMountedCast(Comp_MugirlMount comp, Verb verb, LocalTargetInfo castTarget)
        {
            if (comp == null || verb?.verbProps == null)
            {
                return;
            }

            Pawn carrier = comp.MooPawn;
            if (carrier == null)
            {
                ClearAim(comp);
                return;
            }

            bool fired = verb != null && verb.LastShotTick >= comp.turretCastStartTick;
            ClearAim(comp);

            if (!fired)
            {
                return;
            }

            comp.turretLastAttackedTarget = castTarget;
            comp.turretLastAttackTargetTick = MugirlTickUtility.CurrentGameTickOrFallback(comp.turretLastAttackTargetTick);
            comp.turretBurstCooldownTicksLeft = Mathf.Max(verb.verbProps.AdjustedCooldownTicks(verb, carrier), TurretTickInterval(comp));
        }

        private static int GetMountedAimTicks(Comp_MugirlMount comp, Pawn rider, Verb verb)
        {
            if (verb?.verbProps == null)
            {
                return 0;
            }

            float warmupTime = Mathf.Max(verb.WarmupTime, 0f);
            if (rider != null && warmupTime > 0f)
            {
                warmupTime *= rider.GetStatValue(StatDefOf.AimingDelayFactor);
            }

            return Mathf.Max(warmupTime.SecondsToTicks(), comp?.Props?.turretMinAimTicks ?? 0);
        }

        private static int TurretTickInterval(Comp_MugirlMount comp)
        {
            return Mathf.Max(1, comp?.Props?.turretTickInterval ?? DefaultTurretTickInterval);
        }

        private static void CancelMountedCast(Comp_MugirlMount comp, Verb verb)
        {
            Pawn carrier = comp?.MooPawn;
            if (carrier?.stances?.curStance is Stance_Busy busyStance && (busyStance.verb == verb || (verb == null && IsMountedVerb(busyStance.verb))))
            {
                carrier.stances.SetStance(new Stance_Mobile());
                if (verb == null)
                {
                    verb = busyStance.verb;
                }
            }

            verb?.Reset();
        }

        private static void ClearCarrierMountedStance(Pawn carrier, Verb verb)
        {
            if (carrier?.stances?.curStance is Stance_Busy busyStance && busyStance.verb == verb)
            {
                carrier.stances.SetStance(new Stance_Mobile());
            }
        }

        private static void RestoreCarrierStanceIfMountedVerbChangedIt(Pawn carrier, Verb verb, Stance originalStance)
        {
            if (carrier?.stances == null || originalStance == null)
            {
                return;
            }

            if (carrier.stances.curStance != originalStance && carrier.stances.curStance is Stance_Busy busyStance && busyStance.verb == verb)
            {
                carrier.stances.SetStance(originalStance);
            }
        }

        private static bool WasMountedCastStarted(Comp_MugirlMount comp)
        {
            return comp != null && comp.turretCastStartTick >= 0;
        }

        private static void ClearAim(Comp_MugirlMount comp)
        {
            if (comp == null)
            {
                return;
            }

            comp.turretAimTarget = LocalTargetInfo.Invalid;
            comp.turretAimTicksLeft = 0;
            comp.turretAimTicksTotal = 0;
            comp.turretCastStartTick = -1;
        }

    }
}
