using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using static UnityEngine.GraphicsBuffer;

namespace MooGirl
{
    public class CompRopeToBuild : CompUsable
    {

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
        {

            if (!pawn.CanReserve(this.parent))
            {
                yield return new FloatMenuOption("Reserved", null, MenuOptionPriority.DisabledOption);
            }
            else if (pawn.CanReach(parent, PathEndMode.Touch, Danger.Some))
            {

                var ropee = GetRopeePawn(pawn);
                if (ropee != null && ropee.RaceProps.body == MooGirl_DefOf.MooGirlBody && ropee.CurJob?.def == JobDefOf.FollowRoper && ropee.CurJob.targetA.Thing == pawn)
                {
                    yield return new FloatMenuOption("Rope the colonist to the hitch", () => StartRoping(pawn));
                }
                else
                {
                    yield return new FloatMenuOption("No valid colonist to rope", null, MenuOptionPriority.DisabledOption);
                }
            }
        }


        private void StartRoping(Pawn pawn)
        {
            var ropee = GetRopeePawn(pawn); 
            if (ropee != null && ropee.RaceProps.body == MooGirl_DefOf.MooGirlBody && ropee.CurJob?.def == JobDefOf.FollowRoper && ropee.CurJob.targetA.Thing == pawn)
            {

                Job job = JobMaker.MakeJob(MooGirl_DefOf.RopeToBuild, this.parent);
                job.targetB = ropee;  
                pawn.jobs.StartJob(job); 
            }
        }

        private Pawn GetRopeePawn(Pawn pawn)
        {
            return pawn.Map.mapPawns.AllPawnsSpawned
                .FirstOrDefault(p => p.RaceProps.body == MooGirl_DefOf.MooGirlBody && p.CurJob?.def == JobDefOf.FollowRoper && p.CurJob.targetA.Thing == pawn);
        }
    }



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

            yield return new Toil
            {
                initAction = () =>
                {
                    if (ropee.roping != null)
                    {
                        Pawn roper = ropee.roping?.RopedByPawn;
                        if (roper != null)
                        {
                            roper.roping?.BreakAllRopes();
                        }

                        ropee.roping?.BreakAllRopes(); 

                        if (ropee.jobs != null)
                        {
                            ropee.jobs.ClearQueuedJobs();
                            ropee.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        }

                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

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
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
            yield return Toils_General.Wait(60);
        }
    }


}
