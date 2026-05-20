using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public static class MountedPawnCombatTurret
    {
        private static readonly Dictionary<Verb, Thing> OriginalCasters = new Dictionary<Verb, Thing>();
        private static readonly Dictionary<Verb, float> WarmupTimeOverrides = new Dictionary<Verb, float>();

        public static void NotifyMounted(Comp_MooGirlMount comp)
        {
            Verb verb = GetPrimaryRangedVerb(comp);
            if (verb != null)
            {
                EnsureVerbCaster(comp, verb);
            }
        }

        public static void NotifyDismounting(Comp_MooGirlMount comp)
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

        public static void NotifyDismounting(Comp_MooGirlMount comp, Pawn rider)
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

        public static void VerbTick(Comp_MooGirlMount comp, int delta)
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
                verb.VerbTick();
                RestoreCarrierStanceIfMountedVerbChangedIt(carrier, verb, originalStance);
            }
        }

        public static void Tick(Comp_MooGirlMount comp, int delta)
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

            IAttackTarget target = FindBestTarget(comp, verb);
            if (target?.Thing == null)
            {
                return;
            }

            comp.turretAimTarget = target.Thing;
            comp.turretAimTicksTotal = GetMountedAimTicks(comp, rider, verb);
            comp.turretAimTicksLeft = comp.turretAimTicksTotal;
            comp.turretCastStartTick = -1;
            if (comp.turretAimTicksLeft <= 0 && !TryStartMountedCast(comp, verb, comp.turretAimTarget))
            {
                comp.turretBurstCooldownTicksLeft = Mathf.Max(comp.Props.turretTickInterval, 1);
            }
        }

        public static void TickAim(Comp_MooGirlMount comp, int delta)
        {
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

            if (!IsValidTarget(carrier, verb, comp.turretAimTarget.Thing))
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
                comp.turretBurstCooldownTicksLeft = Mathf.Max(comp.Props.turretTickInterval, 1);
            }
        }

        private static bool TryStartMountedCast(Comp_MooGirlMount comp, Verb verb, LocalTargetInfo castTarget)
        {
            if (comp == null || verb == null || !castTarget.IsValid)
            {
                return false;
            }

            bool started;
            Pawn carrier = comp.MooPawn;
            Stance originalStance = carrier?.stances?.curStance;
            comp.turretCastStartTick = Find.TickManager.TicksGame;
            using (new MountedWarmupOverride(verb, 0f))
            {
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

        private static void FinishMountedCast(Comp_MooGirlMount comp, Verb verb, LocalTargetInfo castTarget)
        {
            Pawn carrier = comp.MooPawn;
            bool fired = verb != null && verb.LastShotTick >= comp.turretCastStartTick;
            ClearAim(comp);

            if (!fired)
            {
                return;
            }

            comp.turretLastAttackedTarget = castTarget;
            comp.turretLastAttackTargetTick = Find.TickManager.TicksGame;
            comp.turretBurstCooldownTicksLeft = Mathf.Max(verb.verbProps.AdjustedCooldownTicks(verb, carrier), comp.Props.turretTickInterval);
        }

        public static bool CanUseMountedRangedWeapon(Comp_MooGirlMount comp, out string reasonKey)
        {
            reasonKey = null;
            Pawn rider = comp?.MountedPawn;
            Pawn carrier = comp?.MooPawn;
            if (comp == null || rider == null || carrier == null || !comp.fireAtWill)
            {
                reasonKey = "MooGirl.Mount.ReasonTurretHoldFire";
                return false;
            }

            if (!carrier.Spawned || carrier.Dead || carrier.Downed || carrier.Destroyed || carrier.IsBurning() || carrier.InMentalState || carrier.stances?.stunner?.Stunned == true)
            {
                reasonKey = "MooGirl.Mount.ReasonTargetBadState";
                return false;
            }

            if (carrier.Faction == null && !carrier.InAggroMentalState)
            {
                reasonKey = "MooGirl.Mount.ReasonTargetBadState";
                return false;
            }

            if (rider.Dead || rider.Downed || rider.InMentalState || rider.IsBurning() || carrier.pather?.Moving == true)
            {
                reasonKey = "MooGirl.Mount.ReasonRiderBadState";
                return false;
            }

            if (!rider.Awake() || !rider.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) || rider.WorkTagIsDisabled(WorkTags.Violent))
            {
                reasonKey = "MooGirl.Mount.ReasonRiderBadState";
                return false;
            }

            if (GetPrimaryRangedVerb(comp) == null)
            {
                reasonKey = "MooGirl.Mount.ReasonNoRangedWeapon";
                return false;
            }

            return true;
        }

        public static void DrawWeapon(Comp_MooGirlMount comp)
        {
            Pawn rider = comp?.MountedPawn;
            Pawn carrier = comp?.MooPawn;
            ThingWithComps weapon = rider?.equipment?.Primary;
            if (rider == null || carrier == null || weapon == null || !weapon.def.IsRangedWeapon)
            {
                return;
            }

            Vector3 drawPos = comp.WeaponDrawPos;
            LocalTargetInfo aimTarget = comp.turretAimTarget;
            Verb mountedVerb = GetPrimaryRangedVerb(comp);
            if (!aimTarget.IsValid && mountedVerb?.state == VerbState.Bursting && mountedVerb.CurrentTarget.IsValid)
            {
                aimTarget = mountedVerb.CurrentTarget;
            }

            if (aimTarget.IsValid)
            {
                Thing targetThing = aimTarget.Thing;
                if (targetThing == null || !targetThing.Destroyed)
                {
                    Vector3 targetPos = aimTarget.HasThing ? targetThing.DrawPos : aimTarget.Cell.ToVector3Shifted();
                    float aimAngle = (targetPos - comp.RiderDrawPos).AngleFlat();
                    drawPos += new Vector3(0f, 0f, 0.4f + weapon.def.equippedDistanceOffset).RotatedBy(aimAngle) * rider.ageTracker.CurLifeStage.equipmentDrawDistanceFactor;
                    PawnRenderUtility.DrawEquipmentAiming(weapon, drawPos, aimAngle);
                    return;
                }
            }

            PawnRenderUtility.DrawCarriedWeapon(weapon, drawPos, carrier.Rotation, rider.ageTracker.CurLifeStage.equipmentDrawDistanceFactor);
        }

        private static Verb GetPrimaryRangedVerb(Comp_MooGirlMount comp)
        {
            return GetPrimaryRangedVerb(comp?.MountedPawn);
        }

        private static Verb GetPrimaryRangedVerb(Pawn rider)
        {
            ThingWithComps weapon = rider?.equipment?.Primary;
            if (weapon == null || !weapon.def.IsRangedWeapon)
            {
                return null;
            }

            Verb verb = weapon.GetComp<CompEquippable>()?.PrimaryVerb;
            if (verb == null || verb.verbProps == null || verb.verbProps.IsMeleeAttack || verb.verbProps.onlyManualCast)
            {
                return null;
            }

            if (verb is IAbilityVerb)
            {
                return null;
            }

            if (verb.EquipmentSource != weapon)
            {
                return null;
            }

            return verb;
        }

        public static bool TryGetWarmupTimeOverride(Verb verb, ref float warmupTime)
        {
            if (verb != null && WarmupTimeOverrides.TryGetValue(verb, out float overrideValue))
            {
                warmupTime = overrideValue;
                return true;
            }

            return false;
        }

        public static bool IsMountedVerb(Verb verb)
        {
            return verb != null && OriginalCasters.ContainsKey(verb);
        }

        private static int GetMountedAimTicks(Comp_MooGirlMount comp, Pawn rider, Verb verb)
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

        private static void CancelMountedCast(Comp_MooGirlMount comp, Verb verb)
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

        private static bool WasMountedCastStarted(Comp_MooGirlMount comp)
        {
            return comp != null && comp.turretCastStartTick >= 0;
        }

        private static void ClearAim(Comp_MooGirlMount comp)
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

        private struct MountedWarmupOverride : System.IDisposable
        {
            private readonly Verb verb;

            public MountedWarmupOverride(Verb verb, float warmupTime)
            {
                this.verb = verb;
                if (verb != null)
                {
                    WarmupTimeOverrides[verb] = warmupTime;
                }
            }

            public void Dispose()
            {
                if (verb != null)
                {
                    WarmupTimeOverrides.Remove(verb);
                }
            }
        }

        private static void EnsureVerbCaster(Comp_MooGirlMount comp, Verb verb)
        {
            Pawn carrier = comp?.MooPawn;
            Pawn rider = comp?.MountedPawn;
            if (carrier == null || verb == null)
            {
                return;
            }

            if (!OriginalCasters.ContainsKey(verb))
            {
                OriginalCasters.Add(verb, verb.caster == carrier && rider != null ? rider : verb.caster);
            }

            if (verb.caster != carrier)
            {
                verb.caster = carrier;
            }
        }

        private static void RestoreVerbCaster(Pawn rider)
        {
            if (rider?.equipment == null)
            {
                return;
            }

            List<ThingWithComps> equipment = rider.equipment.AllEquipmentListForReading;
            for (int i = 0; i < equipment.Count; i++)
            {
                CompEquippable comp = equipment[i].GetComp<CompEquippable>();
                List<Verb> verbs = comp?.AllVerbs;
                if (verbs == null)
                {
                    continue;
                }

                for (int j = 0; j < verbs.Count; j++)
                {
                    Verb verb = verbs[j];
                    if (OriginalCasters.TryGetValue(verb, out Thing originalCaster))
                    {
                        verb.Reset();
                        verb.caster = originalCaster ?? rider;
                        OriginalCasters.Remove(verb);
                    }
                    else if (verb.caster != rider)
                    {
                        verb.Reset();
                        verb.caster = rider;
                    }
                }
            }
        }

        private static IAttackTarget FindBestTarget(Comp_MooGirlMount comp, Verb verb)
        {
            Pawn carrier = comp.MooPawn;
            MountedAttackTargetSearcher searcher = new MountedAttackTargetSearcher(comp, verb);
            TargetScanFlags flags = TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable | TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedNonBurning;
            return AttackTargetFinder.BestShootTargetFromCurrentPosition(searcher, flags, thing => IsValidTarget(carrier, verb, thing), 0f, verb.EffectiveRange);
        }

        private static bool IsValidTarget(Pawn carrier, Verb verb, Thing target)
        {
            if (carrier?.Map == null || verb == null || target == null || target.Destroyed || target.Map != carrier.Map)
            {
                return false;
            }

            if (target == carrier || !carrier.HostileTo(target))
            {
                return false;
            }

            if (target is Pawn pawn && (pawn.Dead || pawn.Downed))
            {
                return false;
            }

            return verb.TryFindShootLineFromTo(carrier.Position, target, out _);
        }

        private class MountedAttackTargetSearcher : IAttackTargetSearcher
        {
            private readonly Comp_MooGirlMount comp;
            private readonly Verb verb;

            public MountedAttackTargetSearcher(Comp_MooGirlMount comp, Verb verb)
            {
                this.comp = comp;
                this.verb = verb;
            }

            public Thing Thing => comp.MooPawn;

            public Verb CurrentEffectiveVerb => verb;

            public LocalTargetInfo LastAttackedTarget => comp.turretLastAttackedTarget;

            public int LastAttackTargetTick => comp.turretLastAttackTargetTick;
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.WarmupTime), MethodType.Getter)]
    public static class Harmony_MountedPawnCombatTurret_WarmupTime
    {
        public static void Postfix(Verb __instance, ref float __result)
        {
            MountedPawnCombatTurret.TryGetWarmupTimeOverride(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(Stance_Warmup), nameof(Stance_Warmup.StanceDraw))]
    public static class Harmony_MountedPawnCombatTurret_StanceWarmupDraw
    {
        public static bool Prefix(Stance_Warmup __instance)
        {
            return !MountedPawnCombatTurret.IsMountedVerb(__instance?.verb);
        }
    }
}
