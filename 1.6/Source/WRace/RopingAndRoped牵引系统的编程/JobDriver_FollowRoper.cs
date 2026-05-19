using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class JobDriver_FollowRoper : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            Toil toil = ToilMaker.MakeToil("MakeNewToils");
            toil.tickIntervalAction = delegate (int delta)
            {
                Pawn pawn = this.job.GetTarget(TargetIndex.A).Thing as Pawn;
                TraverseParms traverseParms = TraverseParms.For(this.pawn, Danger.Deadly, TraverseMode.ByPawn, canBashDoors: false); // 允许开门
                if (!this.pawn.Map.reachability.CanReach(this.pawn.Position, pawn, PathEndMode.Touch, traverseParms))
                {
                    base.EndJobWith(JobCondition.Incompletable);
                    return;
                }


                IntVec3 intVec = IntVec3.Invalid;
                if (pawn.jobs.curDriver is JobDriver_RopeToDestination)
                {
                    intVec = pawn.CurJob.GetTarget(TargetIndex.B).Cell;
                }
                if (intVec.IsValid && pawn.Position == intVec)
                {
                    this.GotoStandCell(intVec);
                    return;
                }
                this.FollowRoper(pawn);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            yield return toil;
            yield break;
        }

        private void FollowRoper(Pawn roper)
        {
            LocalTargetInfo localTargetInfo = new LocalTargetInfo(roper);
            PathEndMode peMode = PathEndMode.Touch;
            if (roper.Position.InHorDistOf(this.pawn.Position, 7.2f) && roper.pather.Moving && roper.pather.curPath != null)
            {
                int marchingPos = Mathf.Abs(roper.roping.Ropees.IndexOf(this.pawn));
                IntVec3 c = this.MarchingOrder(roper, marchingPos);
                if (c.IsValid)
                {
                    localTargetInfo = c;
                    peMode = PathEndMode.OnCell;
                }
            }
            if (!this.pawn.pather.Moving || this.pawn.pather.Destination != localTargetInfo)
            {
                this.pawn.pather.StartPath(localTargetInfo, peMode);
            }
        }

        private IntVec3 MarchingOrder(Pawn roper, int marchingPos)
        {
            PawnPath curPath = roper.pather.curPath;
            if (curPath.NodesLeftCount <= 0)
            {
                return IntVec3.Invalid;
            }
            Map map = this.pawn.Map;
            int num = -2 - marchingPos % 4;
            num = Mathf.Clamp(num, -curPath.NodesConsumedCount, curPath.NodesLeftCount);
            IntVec3 intVec = curPath.Peek(num);
            int num2 = Mathf.Abs(this.pawn.HashOffset()) % 3;
            if (num + 1 < curPath.NodesLeftCount && num2 != 0)
            {
                IntVec3 orig = curPath.Peek(num + 1) - intVec;
                IntVec3 intVec2 = (num2 == 1) ? (intVec + orig.RotatedBy(Rot4.East)) : (intVec + orig.RotatedBy(Rot4.West));
                if (intVec2.InBounds(map) && intVec2.Standable(map) && intVec2.GetDistrict(map, RegionType.Set_Passable) == intVec.GetDistrict(map, RegionType.Set_Passable))
                {
                    intVec = intVec2;
                }
            }
            return intVec;
        }

        private void GotoStandCell(IntVec3 standCell)
        {
            if (!this.pawn.pather.Moving || this.pawn.pather.Destination != standCell)
            {
                this.pawn.pather.StartPath(standCell, PathEndMode.OnCell);
            }
        }

        public override bool IsContinuation(Job j)
        {
            return this.job.GetTarget(TargetIndex.A) == j.GetTarget(TargetIndex.A);
        }

        private const TargetIndex RoperInd = TargetIndex.A;
    }
}
