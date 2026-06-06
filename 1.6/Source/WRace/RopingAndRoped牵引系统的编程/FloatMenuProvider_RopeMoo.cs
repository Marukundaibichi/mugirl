using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class FloatMenuProvider_RopeMoo : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => true;

        public override bool TargetPawnValid(Pawn target, FloatMenuContext context)
        {
            if (!RopingService.IsMooGirlRopee(target))
            {
                return false;
            }

            if (!target.Spawned || target.Dead)
            {
                return false;
            }

            return true;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            Pawn target = clickedPawn;

            if (target == null || pawn == null || pawn == target)
            {
                yield break;
            }

            if (!pawn.Map.reachability.CanReach(pawn.Position, target, PathEndMode.Touch, TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false)))
            {
                yield break;
            }

            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
            {
                yield break;
            }

            if (!target.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
            {
                yield break;
            }

            bool isTargetFollowingRoper = RopingService.IsFollowingRoper(target);
            bool isPawnFollowingRoper = RopingService.IsFollowingRoper(pawn);
            bool isRopedToThing = RopingService.IsRopedToSpot(target);

            // 牵引逻辑。
            if (!isPawnFollowingRoper && (!isTargetFollowingRoper || isRopedToThing) && !target.stances.stunner.Stunned)
            {
                // 旧行为禁止雪牛娘牵引雪牛娘。
                if (RopingService.IsMooGirlRopee(pawn) && RopingService.IsMooGirlRopee(target))
                {
                    yield break;
                }

                // 旧行为禁止牵引狂暴目标。
                if (target.InMentalState && target.MentalState is MentalState_Berserk)
                {
                    yield break;
                }

                Action action = delegate
                {
                    pawn.jobs.TryTakeOrderedJob(new Job(MooGirl_DefOf.JobDriver_RopeMoo, target), JobTag.Misc);
                };

                string label;
                if (RopingService.IsMooGirlRopee(target))
                {
                    label = RopeLabelWithSuccessChance(
                        "MooGirl.Rope.Target".Translate(target).ToString(),
                        1f.ToStringPercent());
                }
                else if (target.IsPrisonerOfColony || target.IsSlave)
                {
                    label = "MooGirl.Rope.Target".Translate(target).ToString();
                }
                else
                {
                    label = RopeLabelWithSuccessChance(
                        "MooGirl.Rope.Target".Translate(target).ToString(),
                        target.GetAcceptArrestChance(pawn).ToStringPercent());
                }

                yield return FloatMenuUtility.DecoratePrioritizedTask(
                    new FloatMenuOption(label, action, MenuOptionPriority.High, null, target, 0f, null, null, true, 0),
                    pawn,
                    target,
                    "ReservedBy",
                    null
                );
            }

            // 解除牵引逻辑。
            if ((isTargetFollowingRoper || isRopedToThing) && !isPawnFollowingRoper)
            {
                yield return new FloatMenuOption("MooGirl.Unrope.Target".Translate(target), () =>
                {
                    pawn.jobs.TryTakeOrderedJob(new Job(MooGirl_DefOf.JobDriver_RemoveRopeMoo, target), JobTag.Misc);
                });
            }
        }

        private static string RopeLabelWithSuccessChance(string baseLabel, string chance)
        {
            return "MooGirl.Rope.TargetWithSuccessChance".Translate(
                baseLabel,
                chance,
                "MooGirl.Rope.SuccessChance".Translate()).ToString();
        }
    }

    public class Utils
    {
        internal static TargetingParameters RopedTarget(Pawn performer)
        {
            return new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = false,
                mapObjectTargetsMustBeAutoAttackable = false,
                validator = delegate (TargetInfo target)
                {
                    if (!target.HasThing)
                    {
                        return false;
                    }

                    Pawn pawn = target.Thing as Pawn;
                    return pawn != null && pawn != performer;
                }
            };
        }
    }
}
