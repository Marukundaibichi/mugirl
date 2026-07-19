using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public class JobDriver_MugirlDunk : JobDriver
    {
        private Thing_MugirlDunkProp prop;

        private Thing PickupTarget => job.GetTarget(TargetIndex.A).Thing;
        private IntVec3 ImpactCell => job.GetTarget(TargetIndex.B).Cell;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Thing target = PickupTarget;
            return target != null
                && target.Spawned
                && pawn.Reserve(target, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(condition =>
            {
                if (prop != null && !prop.Destroyed && !prop.SequenceStarted)
                {
                    prop.TryDropSafely();
                }
            });

            Toil goToObject = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            goToObject.FailOnDestroyedOrNull(TargetIndex.A);
            yield return goToObject;

            Toil pickUp = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    Thing target = PickupTarget;
                    string reason;
                    if (!MugirlThrowUtility.CanPickUp(pawn, target, out reason)
                        || !Thing_MugirlDunkProp.TryCreate(pawn, target, job.ability, out prop))
                    {
                        if (!reason.NullOrEmpty())
                        {
                            Messages.Message(reason, target ?? pawn, MessageTypeDefOf.RejectInput, historical: false);
                        }
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    job.ability?.StartCooldown(job.ability.def.cooldownTicksRange.RandomInRange);
                    IntVec3 approach = FindApproachCell(pawn, ImpactCell);
                    job.SetTarget(TargetIndex.A, approach);
                    job.locomotionUrgency = LocomotionUrgency.Sprint;
                }
            };
            yield return pickUp;

            Toil runUp = Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            runUp.tickAction = () => prop?.NotifyRunning();
            runUp.AddFailCondition(() => prop == null || prop.Destroyed || !prop.HasPayload);
            yield return runUp;

            Toil beginDunk = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    if (prop == null || prop.Destroyed || !prop.BeginDunk(ImpactCell))
                    {
                        EndJobWith(JobCondition.Incompletable);
                    }
                }
            };
            yield return beginDunk;

            Toil waitForFinish = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Never,
                tickAction = () =>
                {
                    if (prop == null || prop.Destroyed || prop.Completed)
                    {
                        ReadyForNextToil();
                    }
                }
            };
            yield return waitForFinish;
        }

        private static IntVec3 FindApproachCell(Pawn pawn, IntVec3 impactCell)
        {
            Map map = pawn.Map;
            IntVec3 direction = pawn.Position - impactCell;
            int dx = direction.x == 0 ? 0 : (direction.x > 0 ? 1 : -1);
            int dz = direction.z == 0 ? 0 : (direction.z > 0 ? 1 : -1);
            IntVec3 preferred = impactCell + new IntVec3(dx * 3, 0, dz * 3);

            if (IsValidApproach(pawn, preferred, map))
            {
                return preferred;
            }

            IntVec3 result;
            if (CellFinder.TryFindRandomCellNear(preferred.ClampInsideMap(map), map, 3, cell => IsValidApproach(pawn, cell, map), out result))
            {
                return result;
            }

            return pawn.Position;
        }

        private static bool IsValidApproach(Pawn pawn, IntVec3 cell, Map map)
        {
            return cell.IsValid
                && cell.InBounds(map)
                && cell.Standable(map)
                && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref prop, "dunkProp");
        }
    }
}
