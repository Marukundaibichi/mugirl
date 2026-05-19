using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class JobDriver_RemoveRopeMoo : JobDriver
    {
        private Pawn Target => (Pawn)this.job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return this.pawn.Reserve(this.Target, this.job, 1, -1, null, errorOnFailed, false);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                .FailOn(() => !this.pawn.CanReach(this.Target, PathEndMode.Touch, Danger.Deadly));

            yield return Toils_General.Wait(50)
                .WithProgressBarToilDelay(TargetIndex.A);

            yield return new Toil
            {
                initAction = () =>
                {
                    Pawn roper = Target.roping?.RopedByPawn;
                    if (roper != null)
                    {
                        roper.roping?.BreakAllRopes();
                    }
                    Target.roping?.BreakAllRopes();

                    Target.jobs.ClearQueuedJobs();
                    Target.jobs.EndCurrentJob(JobCondition.InterruptForced);

                }
            };
        }
    }

}
