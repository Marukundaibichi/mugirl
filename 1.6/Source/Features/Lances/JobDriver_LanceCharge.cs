using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Mugirl.Features.Lances
{
    public sealed class JobDriver_LanceCharge : JobDriver
    {
        private LocalTargetInfo ChargeTarget => job.GetTarget(TargetIndex.A);
        private LocalTargetInfo PointTargetCell => job.GetTarget(TargetIndex.B);
        private Pawn TargetPawn => job.GetTarget(TargetIndex.A).Thing as Pawn;
        private bool IsPointCharge => job.def == Mugirl_DefOf.Job_MugirlLancePointCharge;
        private ThingWithComps Lance => pawn?.equipment?.Primary;
        private CompLanceCharge LanceComp => Lance?.TryGetComp<CompLanceCharge>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!IsPointCharge)
            {
                return ChargeTarget.IsValid;
            }

            Pawn target = TargetPawn;
            // 冲锋在起飞时锁定目标引用，不需要独占敌人；强行预约敌对 Pawn 会偶发失败并产生任务警告。
            return target != null && !target.Destroyed && !target.Dead;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            if (IsPointCharge)
            {
                this.FailOnDestroyedOrNull(TargetIndex.A);
                this.FailOn(() => TargetPawn == null || TargetPawn.Dead || LanceComp == null);
            }
            else
            {
                this.FailOn(() => !ChargeTarget.IsValid || pawn.Map == null
                    || !ChargeTarget.Cell.InBounds(pawn.Map) || LanceComp == null);
            }

            Toil charge = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    // PawnFlyer 会挂起当前任务；落地恢复后 count 保留为 1，避免再次发射。
                    if (job.count <= 0 && !TryLaunchDirectCharge())
                    {
                        EndJobWith(JobCondition.Incompletable);
                    }
                }
            };
            yield return charge;

        }

        private bool TryLaunchDirectCharge()
        {
            Map map = pawn.Map;
            CompLanceCharge comp = LanceComp;
            ThingDef flyerDef = Mugirl_DefOf.Mugirl_LanceChargeFlyer;
            IntVec3 targetCell = IsPointCharge && PointTargetCell.IsValid
                ? PointTargetCell.Cell
                : ChargeTarget.Cell;
            if (map == null || comp == null || flyerDef == null
                || !typeof(PawnFlyer_LanceCharge).IsAssignableFrom(flyerDef.thingClass)
                || !LanceChargeDestinationUtility.TryFindLandingCell(pawn, targetCell, IsPointCharge, out IntVec3 destination))
            {
                return false;
            }

            Vector3 direction = (targetCell - pawn.Position).ToVector3();
            if (direction.sqrMagnitude > 0.001f)
            {
                pawn.Rotation = Rot4.FromAngleFlat(direction.AngleFlat());
            }

            job.count = 1;
            PawnFlyer_LanceCharge flyer = PawnFlyer.MakeFlyer(
                flyerDef,
                pawn,
                destination,
                null,
                null) as PawnFlyer_LanceCharge;
            if (flyer == null)
            {
                job.count = 0;
                return false;
            }

            flyer.Configure(!IsPointCharge, comp.Props.steamTrail, IsPointCharge ? TargetPawn : null);
            GenSpawn.Spawn(flyer, destination, map, WipeMode.Vanish);
            MugirlSelectionUtility.ReselectIfSelectedInPlaying(
                pawn,
                playSound: false,
                forceDesignatorDeselect: false);
            return true;
        }

    }
}
