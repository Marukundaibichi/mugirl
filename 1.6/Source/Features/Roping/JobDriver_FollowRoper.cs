using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Mugirl
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
            toil.tickIntervalAction = delegate
            {
                Pawn roper = this.job.GetTarget(TargetIndex.A).Thing as Pawn;
                if (roper == null)
                {
                    base.EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // 兼容旧存档及异常中断留下的孤儿跟随 Job：
                // AI 跟随不能脱离 RopeTracker 的实际牵引关系独立存在。
                if (this.pawn.roping?.RopedByPawn != roper)
                {
                    base.EndJobWith(JobCondition.Incompletable);
                    return;
                }

                TraverseParms traverseParms = TraverseParms.For(this.pawn, Danger.Deadly, TraverseMode.ByPawn, canBashDoors: false);
                if (!this.pawn.Map.reachability.CanReach(this.pawn.Position, roper, PathEndMode.Touch, traverseParms))
                {
                    base.EndJobWith(JobCondition.Incompletable);
                    return;
                }

                IntVec3 standCell = IntVec3.Invalid;
                if (roper.jobs.curDriver is JobDriver_RopeToDestination)
                {
                    standCell = roper.CurJob.GetTarget(TargetIndex.B).Cell;
                }

                if (standCell.IsValid && roper.Position == standCell)
                {
                    GotoStandCell(standCell);
                    return;
                }

                FollowRoper(roper);
            };
            toil.defaultCompleteMode = ToilCompleteMode.Never;
            yield return toil;
        }

        private void FollowRoper(Pawn roper)
        {
            if (roper?.roping?.Ropees == null)
            {
                base.EndJobWith(JobCondition.Incompletable);
                return;
            }

            LocalTargetInfo localTargetInfo = new LocalTargetInfo(roper);
            PathEndMode peMode = PathEndMode.Touch;
            if (roper.Position.InHorDistOf(this.pawn.Position, 7.2f) && roper.pather.Moving && roper.pather.curPath != null)
            {
                int marchingPos = Mathf.Abs(roper.roping.Ropees.IndexOf(this.pawn));
                IntVec3 cell = MarchingOrder(roper, marchingPos);
                if (cell.IsValid)
                {
                    localTargetInfo = cell;
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
                IntVec3 intVec2 = num2 == 1 ? intVec + orig.RotatedBy(Rot4.East) : intVec + orig.RotatedBy(Rot4.West);
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
