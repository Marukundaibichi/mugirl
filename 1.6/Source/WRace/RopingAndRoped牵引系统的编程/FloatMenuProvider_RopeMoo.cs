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
            if (target.RaceProps.body != MooGirl_DefOf.MooGirlBody)
                return false;
            if (!target.Spawned || target.Dead)
                return false;
            return true;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            Pawn target = clickedPawn;

            if (target == null || pawn == null || pawn == target)
                yield break;

            if (!pawn.Map.reachability.CanReach(pawn.Position, target, PathEndMode.Touch, TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, false)))
                yield break;

            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
                yield break;

            if (!target.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
                yield break;

            Pawn currentRoper = target.roping?.RopedByPawn;
            bool isTargetFollowingRoper = target.CurJob?.def == MooGirl_DefOf.Job_FollowRoper;
            bool isPawnFollowingRoper = pawn.CurJob?.def == MooGirl_DefOf.Job_FollowRoper;
            bool isRopedToThing = target.roping?.IsRopedToSpot ?? false;

            // Rope 逻辑
            if (!isPawnFollowingRoper && (!isTargetFollowingRoper || isRopedToThing) && !target.stances.stunner.Stunned)
            {
                // ❌ 禁止雪牛娘牵引雪牛娘
                if (pawn.RaceProps?.body == MooGirl_DefOf.MooGirlBody &&
                    target.RaceProps?.body == MooGirl_DefOf.MooGirlBody)
                {
                    // 不生成任何菜单选项
                    yield break;
                }

                // ❌ 禁止牵引处于狂暴状态的目标
                if (target.InMentalState && target.MentalState is MentalState_Berserk)
                {
                    yield break;
                }

                // 生成行动
                Action action = delegate
                {
                    pawn.jobs.TryTakeOrderedJob(new Job(MooGirl_DefOf.JobDriver_RopeMoo, target), JobTag.Misc);
                };

                string label;
                if (target.RaceProps?.body == MooGirl_DefOf.MooGirlBody)
                {
                    label = "MooGirl.Rope.Target".Translate(target)
                            + " (100% "
                            + "MooGirl.Rope.SuccessChance".Translate() + ")";
                }
                else if (target.IsPrisonerOfColony || target.IsSlave)
                {
                    // 目标是囚犯或奴隶，显示简单标签
                    label = "MooGirl.Rope.Target".Translate(target);
                }
                else
                {
                    label = "MooGirl.Rope.Target".Translate(target)
                            + " (" + target.GetAcceptArrestChance(pawn).ToStringPercent() + " "
                            + "MooGirl.Rope.SuccessChance".Translate() + ")";
                }

                yield return FloatMenuUtility.DecoratePrioritizedTask(
                    new FloatMenuOption(label, action, MenuOptionPriority.High, null, target, 0f, null, null, true, 0),
                    pawn,
                    target,
                    "ReservedBy",
                    null
                );
            }



            // Unrope 逻辑
            if ((isTargetFollowingRoper || isRopedToThing) && !isPawnFollowingRoper)
            {
                yield return new FloatMenuOption("MooGirl.Unrope.Target".Translate(target), () =>
                {
                    pawn.jobs.TryTakeOrderedJob(new Job(MooGirl_DefOf.JobDriver_RemoveRopeMoo, target), JobTag.Misc);
                });
            }
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
