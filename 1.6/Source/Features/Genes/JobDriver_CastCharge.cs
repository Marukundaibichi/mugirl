using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public class JobDriver_CastCharge : JobDriver
    {
        private const float SmallPawnThreshold = 1f;
        private const float ImpactRadius = 2f;
        private const int SmokeGenerationInterval = 10;
        private const int FinalKnockbackDistance = 6;
        private static readonly VerbProperties DefaultJumpVerbProps = new VerbProperties();

        private readonly HashSet<Pawn> hitPawns = new HashSet<Pawn>();
        private int ticksSinceSmokeGeneration;

        private Pawn TargetPawn => job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn target = TargetPawn;
            return CanChargeTarget(target) && pawn.Reserve(target, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => !CanChargeTarget(TargetPawn));

            AddFinishAction(_ => RemoveChargeHediff());
            EnsureChargeHediff();
            GenerateSmokeBehindPawn(TargetPawn);

            Toil gotoToil = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            gotoToil.tickAction = () =>
            {
                ApplyPassingImpact(TargetPawn);
                TickSmokeTrail();
            };
            yield return gotoToil;

            Toil attack = new Toil();
            attack.initAction = () => ApplyFinalImpact(TargetPawn);
            attack.defaultCompleteMode = ToilCompleteMode.Instant;

            if (job.ability != null)
            {
                job.ability.StartCooldown(job.ability.def.cooldownTicksRange.RandomInRange);
            }

            yield return attack;
        }

        private bool CanChargeTarget(Pawn target)
        {
            return pawn != null
                && target != null
                && target != pawn
                && target.Spawned
                && !target.Destroyed
                && !target.Dead
                && target.Map == pawn.Map;
        }

        private void ApplyPassingImpact(Pawn target)
        {
            if (pawn.Map == null)
            {
                return;
            }

            // 每 tick 只遍历能力半径内的实体，避免在热路径上分配过滤枚举对象。
            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, ImpactRadius, useCenter: true))
            {
                Pawn hitPawn = thing as Pawn;
                if (hitPawn == null || hitPawn == pawn || hitPawn == target || hitPawns.Contains(hitPawn))
                {
                    continue;
                }

                DamageInfo damageInfo = hitPawn.BodySize < SmallPawnThreshold
                    ? new DamageInfo(DamageDefOf.Crush, Rand.Range(3f, 5f), 0f, -1, pawn)
                    : new DamageInfo(DamageDefOf.Blunt, Rand.Range(2f, 3f), 0f, -1, pawn);

                hitPawn.TakeDamage(damageInfo);
                hitPawns.Add(hitPawn);

                if (!hitPawn.Dead)
                {
                    IntVec3 flyTargetPosition = hitPawn.Position + GetDirectionFromCaster(hitPawn).ToIntVec3();
                    DoJump(hitPawn, flyTargetPosition, DefaultJumpVerbProps);
                }
            }
        }

        private void ApplyFinalImpact(Pawn target)
        {
            RemoveChargeHediff();

            if (!CanChargeTarget(target))
            {
                return;
            }

            target.TakeDamage(new DamageInfo(DamageDefOf.Cut, 50f, 0f, -1, pawn));
            target.health.AddHediff(HediffMaker.MakeHediff(Mugirl_DefOf.Mugirl_Stun, target));

            if (!target.Dead)
            {
                IntVec3 flyTargetPosition = target.Position + GetDirectionFromCaster(target).ToIntVec3() * FinalKnockbackDistance;
                DoJump(target, flyTargetPosition, DefaultJumpVerbProps);
            }
        }

        private void EnsureChargeHediff()
        {
            if (pawn?.health == null || Mugirl_DefOf.Mugirl_Charge == null)
            {
                return;
            }

            if (pawn.health.hediffSet.GetFirstHediffOfDef(Mugirl_DefOf.Mugirl_Charge) == null)
            {
                pawn.health.AddHediff(HediffMaker.MakeHediff(Mugirl_DefOf.Mugirl_Charge, pawn));
            }
        }

        private void RemoveChargeHediff()
        {
            if (pawn?.health == null || Mugirl_DefOf.Mugirl_Charge == null)
            {
                return;
            }

            Hediff speedHediff;
            while ((speedHediff = pawn.health.hediffSet.GetFirstHediffOfDef(Mugirl_DefOf.Mugirl_Charge)) != null)
            {
                pawn.health.RemoveHediff(speedHediff);
            }
        }

        private Vector3 GetDirectionFromCaster(Pawn target)
        {
            return (target.Position.ToVector3() - pawn.Position.ToVector3()).normalized;
        }

        private void GenerateSmokeBehindPawn(Pawn target)
        {
            if (pawn.Map == null || target == null)
            {
                return;
            }

            int smokeCount = Rand.RangeInclusive(3, 5);
            for (int i = 0; i < smokeCount; i++)
            {
                Vector3 direction = (pawn.DrawPos - target.DrawPos).normalized;
                IntVec3 smokePosition = pawn.Position + direction.ToIntVec3() * (i + 1);
                FleckMaker.ThrowSmoke(smokePosition.ToVector3Shifted(), pawn.Map, Rand.Range(1f, 1.5f));
            }
        }

        private void TickSmokeTrail()
        {
            if (++ticksSinceSmokeGeneration < SmokeGenerationInterval)
            {
                return;
            }

            ticksSinceSmokeGeneration = 0;
            if (pawn.Map != null)
            {
                FleckMaker.ThrowSmoke(pawn.Position.ToVector3Shifted(), pawn.Map, Rand.Range(1.5f, 2f));
            }
        }

        public static bool DoJump(Pawn pawn, IntVec3 targetPosition, VerbProperties verbProps, Ability triggeringAbility = null, LocalTargetInfo target = default(LocalTargetInfo), ThingDef pawnFlyerOverride = null)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return false;
            }

            IntVec3 position = pawn.Position;
            Map map = pawn.Map;
            if (map == null || !TryDismountMountedRiderBeforeJump(pawn, map))
            {
                return false;
            }

            if (!targetPosition.IsValid || !targetPosition.InBounds(map))
            {
                return false;
            }

            PawnFlyer pawnFlyer = PawnFlyer.MakeFlyer(
                pawnFlyerOverride ?? ThingDefOf.PawnFlyer,
                pawn,
                targetPosition,
                verbProps?.flightEffecterDef,
                verbProps?.soundLanding,
                false,
                null,
                triggeringAbility,
                target);

            if (pawnFlyer == null)
            {
                return false;
            }

            FleckMaker.ThrowDustPuff(position.ToVector3Shifted() + Gen.RandomHorizontalVector(0.5f), map, 2f);
            GenSpawn.Spawn(pawnFlyer, targetPosition, map, WipeMode.Vanish);

            MugirlSelectionUtility.ReselectIfSelectedInPlaying(pawn, playSound: false, forceDesignatorDeselect: false);

            return true;
        }

        private static bool TryDismountMountedRiderBeforeJump(Pawn pawn, Map map)
        {
            Comp_MugirlMount comp = MountedPawnUtility.GetMountComp(pawn);
            if (comp?.MountedPawn == null)
            {
                return true;
            }

            if (comp.TryDismount(sendMessage: false))
            {
                return true;
            }

            return comp.TryEmergencyDismountNear(pawn.Position, map) || comp.MountedPawn == null;
        }
    }
}
