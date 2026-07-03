using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public class FloatMenuProvider_RopeMoo : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => true;

        public override bool TargetPawnValid(Pawn target, FloatMenuContext context)
        {
            if (!RopingService.IsMugirlRopee(target))
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

            if (pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) != true)
            {
                yield break;
            }

            if (target.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) != true)
            {
                yield break;
            }

            bool isTargetRopedByPawn = RopingService.IsRopedByPawn(target);
            bool isPawnRopedByPawn = RopingService.IsRopedByPawn(pawn);
            bool isPawnRopedToThing = RopingService.IsRopedToSpot(pawn);
            bool isRopedToThing = RopingService.IsRopedToSpot(target);

            if (RopingService.CanStartPawnRope(pawn, target))
            {
                Action action = delegate
                {
                    pawn.jobs.TryTakeOrderedJob(new Job(Mugirl_DefOf.JobDriver_RopeMoo, target), JobTag.Misc);
                };

                string targetLabel = target.LabelShortCap;
                string label = RopeLabelWithSuccessChance(
                    "Mugirl.Rope.Target".Translate(targetLabel).ToString(),
                    1f.ToStringPercent());

                yield return FloatMenuUtility.DecoratePrioritizedTask(
                    new FloatMenuOption(label, action, MenuOptionPriority.High, null, target, 0f, null, null, true, 0),
                    pawn,
                    target,
                    "ReservedBy",
                    null
                );
            }

            if ((isTargetRopedByPawn || isRopedToThing) && !isPawnRopedByPawn && !isPawnRopedToThing)
            {
                yield return new FloatMenuOption("Mugirl.Unrope.Target".Translate(target.LabelShortCap), () =>
                {
                    pawn.jobs.TryTakeOrderedJob(new Job(Mugirl_DefOf.JobDriver_RemoveRopeMoo, target), JobTag.Misc);
                });
            }
        }

        private static string RopeLabelWithSuccessChance(string baseLabel, string chance)
        {
            return "Mugirl.Rope.TargetWithSuccessChance".Translate(
                baseLabel,
                chance,
                "Mugirl.Rope.SuccessChance".Translate()).ToString();
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
