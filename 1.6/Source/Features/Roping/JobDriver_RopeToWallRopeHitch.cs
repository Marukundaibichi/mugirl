using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public class JobDriver_RopeToWallRopeHitch : JobDriver
    {
        private Map pendingSpotRopeMap;

        protected Building WallRopeHitch => job.GetTarget(TargetIndex.A).Thing as Building;
        protected Pawn RopeePawn => job.GetTarget(TargetIndex.B).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Building hitch = WallRopeHitch;
            Pawn ropee = RopeePawn;
            return CanStartRopeToHitch(pawn, ropee, hitch) &&
                    pawn.Reserve(hitch, job, 1, -1, null, errorOnFailed) &&
                    pawn.Reserve(ropee, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Pawn ropee = RopeePawn;
            Building hitch = WallRopeHitch;

            if (!CanStartRopeToHitch(pawn, ropee, hitch))
            {
                yield break;
            }

            AddFinishAction(_ => ClearPendingSpotRope(ropee));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                .FailOn(() => !CanPlaceAtHitch(pawn, ropee, hitch) || !this.pawn.CanReach(hitch, PathEndMode.Touch, Danger.Deadly));

            yield return new Toil
            {
                initAction = () =>
                {
                    if (!CanStartRopeToHitch(pawn, ropee, hitch))
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

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

                    pendingSpotRopeMap = ropee.Map;
                    RopingService.MarkPendingSpotRope(ropee);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            yield return new Toil
            {
                initAction = () =>
                {
                    if (!CanPlaceAtHitch(pawn, ropee, hitch))
                    {
                        ClearPendingSpotRope(ropee);
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    if (ropee.Map != null)
                    {
                        ropee.Map.pawnDestinationReservationManager.ReleaseAllClaimedBy(ropee);
                    }

                    ropee.Position = hitch.Position;

                    if (ropee.roping == null)
                    {
                        ropee.roping = new Pawn_RopeTracker(ropee);
                    }

                    if (ropee.roping != null)
                    {
                        ropee.roping.RopeToSpot(hitch.Position);
                        RopingService.RegisterRopedToSpot(ropee);
                    }

                    ClearPendingSpotRope(ropee);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            yield return Toils_General.Wait(60);
        }

        private static bool CanStartRopeToHitch(Pawn roper, Pawn ropee, Building hitch)
        {
            return CanPlaceAtHitch(roper, ropee, hitch) &&
                RopingService.IsMugirlRopee(ropee) &&
                RopingService.IsFollowingRoper(ropee) &&
                ropee.CurJob?.targetA.Thing == roper;
        }

        private static bool CanPlaceAtHitch(Pawn roper, Pawn ropee, Building hitch)
        {
            return roper != null &&
                ropee != null &&
                hitch != null &&
                roper.Spawned &&
                ropee.Spawned &&
                hitch.Spawned &&
                !roper.Dead &&
                !ropee.Dead &&
                !roper.Destroyed &&
                !ropee.Destroyed &&
                !hitch.Destroyed &&
                roper.Map == hitch.Map &&
                ropee.Map == hitch.Map;
        }

        private void ClearPendingSpotRope(Pawn ropee)
        {
            RopingService.ClearPendingSpotRope(ropee, pendingSpotRopeMap);
            pendingSpotRopeMap = null;
        }
    }
}
