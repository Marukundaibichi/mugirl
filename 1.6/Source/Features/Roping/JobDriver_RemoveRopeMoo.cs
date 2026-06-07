using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class JobDriver_RemoveRopeMoo : JobDriver
    {
        private Pawn Target => this.job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn target = Target;
            return target != null && this.pawn.Reserve(target, this.job, 1, -1, null, errorOnFailed, false);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            if (Target == null)
            {
                yield break;
            }

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                .FailOn(() => Target == null || !RopingService.HasAnyRope(Target) || !this.pawn.CanReach(this.Target, PathEndMode.Touch, Danger.Deadly));

            yield return Toils_General.Wait(50)
                .WithProgressBarToilDelay(TargetIndex.A)
                .FailOn(() => Target == null || !RopingService.HasAnyRope(Target));

            yield return new Toil
            {
                initAction = () =>
                {
                    Pawn target = Target;
                    if (target == null)
                    {
                        return;
                    }

                    if (!RopingService.HasAnyRope(target))
                    {
                        return;
                    }

                    Pawn roper = RopingService.RoperFor(target);
                    RopingService.BreakAllRopesAndNotify(roper);
                    RopingService.BreakAllRopesAndNotify(target);
                    RopingService.ClearPendingSpotRope(target);

                    if (target.jobs != null)
                    {
                        target.jobs.ClearQueuedJobs();
                        target.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    }

                }
            };
        }
    }

}
