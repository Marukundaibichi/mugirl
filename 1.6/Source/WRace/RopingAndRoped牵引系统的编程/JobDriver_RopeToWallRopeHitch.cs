using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class JobDriver_RopeToWallRopeHitch : JobDriver
    {
        protected Building WallRopeHitch => (Building)job.GetTarget(TargetIndex.A).Thing;
        protected Pawn RopeePawn => (Pawn)job.GetTarget(TargetIndex.B).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(WallRopeHitch, job, 1, -1, null) &&
                   pawn.Reserve(RopeePawn, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Pawn ropee = RopeePawn;
            Building hitch = WallRopeHitch;

            if (ropee == null || hitch == null)
            {
                yield break;
            }

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                .FailOn(() => !this.pawn.CanReach(hitch, PathEndMode.Touch, Danger.Deadly));

            // 断绳前标记“正在拴绳”
            yield return new Toil
            {
                initAction = () =>
                {
                    RopingService.MarkPendingSpotRope(ropee);

                    if (ropee.roping != null)
                    {
                        Pawn roper = RopingService.RoperFor(ropee);
                        RopingService.BreakAllRopesAndNotify(roper);
                        RopingService.BreakAllRopesAndNotify(ropee);

                        if (ropee.jobs != null)
                        {
                            ropee.jobs.ClearQueuedJobs();
                            ropee.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        }
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            // 拴到墙体
            yield return new Toil
            {
                initAction = () =>
                {
                    if (ropee.Map != null)
                    {
                        ropee.Map.pawnDestinationReservationManager.ReleaseAllClaimedBy(ropee);
                    }

                    ropee.Position = hitch.Position;

                    if (ropee.roping != null)
                    {
                        ropee.roping.RopeToSpot(hitch.Position);
                        RopingService.RegisterRopedToSpot(ropee);
                    }

                    // 绑定完成后清除状态
                    RopingService.ClearPendingSpotRope(ropee);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            yield return Toils_General.Wait(60);
        }
    }
}
