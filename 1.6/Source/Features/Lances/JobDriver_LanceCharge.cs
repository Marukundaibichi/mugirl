using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Mugirl.Features.Lances
{
    public sealed class JobDriver_LanceCharge : JobDriver
    {
        private const float PassingAttackRadius = 1.42f;
        private readonly HashSet<Thing> hitThings = new HashSet<Thing>();
        private int smokeTick;

        private Pawn TargetPawn => job.GetTarget(TargetIndex.A).Thing as Pawn;
        private bool IsPointCharge => job.def == Mugirl_DefOf.Job_MugirlLancePointCharge;
        private ThingWithComps Lance => pawn?.equipment?.Primary;
        private CompLanceCharge LanceComp => Lance?.TryGetComp<CompLanceCharge>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn target = TargetPawn;
            return target != null && pawn.Reserve(target, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(() => TargetPawn == null || TargetPawn.Dead || LanceComp == null);

            Toil charge = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            charge.tickAction = () =>
            {
                if (!IsPointCharge)
                {
                    AttackPassingTargets();
                }

                if (LanceComp?.Props.steamTrail == true && ++smokeTick >= 8)
                {
                    smokeTick = 0;
                    FleckMaker.ThrowSmoke(pawn.Position.ToVector3Shifted(), pawn.Map, Rand.Range(0.8f, 1.25f));
                }
            };
            yield return charge;

            Toil impact = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    if (IsPointCharge)
                    {
                        ApplyPointImpact(TargetPawn);
                    }
                    else
                    {
                        ApplyLineImpact(TargetPawn);
                    }
                }
            };
            yield return impact;
        }

        private void AttackPassingTargets()
        {
            Map map = pawn.Map;
            if (map == null)
            {
                return;
            }

            foreach (Thing thing in GenRadial.RadialDistinctThingsAround(pawn.Position, map, PassingAttackRadius, true))
            {
                if (!(thing is Pawn target) || !CanHit(target) || hitThings.Contains(target))
                {
                    continue;
                }

                ApplyLineImpact(target);
            }
        }

        private void ApplyLineImpact(Pawn target)
        {
            if (!CanHit(target) || hitThings.Contains(target))
            {
                return;
            }

            hitThings.Add(target);
            CompProperties_LanceCharge properties = LanceComp.Props;
            Tool tool = Lance.def.tools.NullOrEmpty() ? null : Lance.def.tools[0];
            float damage = tool?.AdjustedBaseMeleeDamageAmount(Lance, DamageDefOf.Stab) ?? 10f;
            damage *= pawn.GetStatValue(StatDefOf.MeleeDamageFactor);
            DamageInfo damageInfo = new DamageInfo(DamageDefOf.Stab, damage, tool?.armorPenetration ?? 0.5f, -1f, pawn, null, Lance.def);
            damageInfo.SetTool(tool);
            target.TakeDamage(damageInfo);
            Stun(target, properties.lineStunTicks);
            DamageLance(properties.lineDurabilityCost);
            MountedPawnMeleeSupport.TryAttackAlongCharge(MountedPawnUtility.GetMountComp(pawn), target);
        }

        private void ApplyPointImpact(Pawn target)
        {
            if (!CanHit(target))
            {
                return;
            }

            CompProperties_LanceCharge properties = LanceComp.Props;
            float damage = Mathf.Min(Lance.HitPoints, properties.maximumPointDamage);
            damage *= pawn.GetStatValue(StatDefOf.MeleeDamageFactor);
            target.TakeDamage(new DamageInfo(DamageDefOf.Stab, damage, 0.5f, -1f, pawn, null, Lance.def));
            Stun(target, properties.pointStunTicks);
            DamageLance(properties.pointDurabilityCost);
            MountedPawnMeleeSupport.TryAttackAlongCharge(MountedPawnUtility.GetMountComp(pawn), target);
        }

        private bool CanHit(Pawn target)
        {
            return target?.Spawned == true && !target.Dead && target != pawn && target.Map == pawn.Map && pawn.HostileTo(target);
        }

        private void Stun(Pawn target, int ticks)
        {
            if (ticks > 0 && target?.stances?.stunner != null && !target.Dead)
            {
                target.stances.stunner.StunFor(ticks, pawn);
            }
        }

        private void DamageLance(int amount)
        {
            if (amount > 0 && Lance != null && !Lance.Destroyed)
            {
                Lance.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, amount));
            }
        }
    }
}
