using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public class JobDriver_RopeMoo : JobDriver
    {
        private Pawn Target => this.job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn target = Target;
            return RopingService.CanStartPawnRope(this.pawn, target) && this.pawn.Reserve(target, this.job, 1, -1, null, errorOnFailed, false);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            if (Target == null)
            {
                yield break;
            }

            this.FailOn(() => !RopingService.CanStartPawnRope(pawn, Target));
            yield return Toils_Reserve.Reserve(TargetIndex.A, 1, -1, null, false);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch, false)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            yield return Toils_RopeMoo.RopePawn(TargetIndex.A);
        }

    }
    public static class Toils_RopeMoo
    {
        public static Toil RopePawn(TargetIndex ropeeInd)
        {
            Toil toil = ToilMaker.MakeToil("RopePawn");

            toil.initAction = delegate ()
            {
                Pawn actor = toil.actor;
                Pawn pawn = actor.jobs.curJob.GetTarget(ropeeInd).Thing as Pawn;

                if (pawn != null)
                {
                    if (!RopingService.CanStartPawnRope(actor, pawn))
                    {
                        actor.jobs.EndCurrentJob(JobCondition.Incompletable, true, true);
                        return;
                    }

                    if (!pawn.IsSlave && !pawn.IsPrisonerOfColony)
                    {
                        NotifyRopeAccepted(pawn);
                    }

                    // 牵引逻辑：初始化 RopeTracker 并执行牵引
                    if (pawn.roping == null)
                    {
                        pawn.roping = new Pawn_RopeTracker(pawn);
                    }

                    if (actor.roping == null)
                    {
                        actor.roping = new Pawn_RopeTracker(actor);
                    }

                    actor.roping.RopePawn(pawn);
                    RopingService.RegisterPawnRope(actor, pawn);

                    Pawn_CallTracker caller = pawn.caller;
                    if (caller != null)
                    {
                        caller.DoCall();
                    }

                    PawnUtility.ForceWait(pawn, 30, actor, false, false);
                }
            };

            toil.defaultCompleteMode = ToilCompleteMode.Delay;
            toil.defaultDuration = 30;
            toil.FailOnDespawnedOrNull(ropeeInd);
            toil.PlaySustainerOrSound(() => SoundDefOf.Roping, 1f);
            return toil;
        }

        private static void NotifyRopeAccepted(Pawn ropee)
        {
            foreach (var comp in ropee.AllComps)
            {
                comp.Notify_Arrested(true);
            }
        }

    }
}
