using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public class JobDriver_ForceUnlockSlaveApparel : JobDriver
    {
        private const TargetIndex TargetPawnInd = TargetIndex.A;
        private const TargetIndex ApparelInd = TargetIndex.B;

        private Pawn TargetPawn
        {
            get
            {
                LocalTargetInfo target = job.GetTarget(TargetPawnInd);
                return target.HasThing ? target.Thing as Pawn : null;
            }
        }

        private Apparel TargetApparel
        {
            get
            {
                LocalTargetInfo target = job.GetTarget(ApparelInd);
                return target.HasThing ? target.Thing as Apparel : null;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn targetPawn = TargetPawn;
            Apparel targetApparel = TargetApparel;
            if (!CanForceUnlockNow(pawn, targetPawn, targetApparel))
            {
                return false;
            }

            bool reserveTarget = targetPawn == pawn || pawn.Reserve(targetPawn, job, 1, -1, null, errorOnFailed);
            bool reserveApparel = pawn.Reserve(targetApparel, job, 1, -1, null, errorOnFailed);
            return reserveTarget && reserveApparel;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Pawn wearer = TargetPawn;
            Apparel apparel = TargetApparel;
            bool unlockSelf = wearer == pawn;

            // JobDriver.ExposeData 会强制保存 curToilIndex，并在读档时重新执行 MakeNewToils；
            // 读档阶段 pawn 尚未生成，CanReach 等依赖生成状态的判定必然失败。若在此处按这些
            // 条件裁剪 toil，读档后 toil 数量就会小于存档里的下标，触发
            // "tried to get CurToil with curToilIndex=N but only has M toils"。
            // 因此 toil 数量只依赖存档稳定的条件（wearer 是否为施术者本人），
            // 有效性一律交给 FailOn 在运行时判定。
            this.FailOn(() => !CanForceUnlockNow(pawn, wearer, apparel));

            if (!unlockSelf)
            {
                yield return Toils_Reserve.Reserve(TargetPawnInd);
            }

            yield return Toils_Reserve.Reserve(ApparelInd);

            if (!unlockSelf)
            {
                yield return Toils_Goto.GotoThing(TargetPawnInd, PathEndMode.Touch);
            }

            int woundTickCounter = 0;
            Toil wait = Toils_General.WaitWith(TargetPawnInd, ForceUnlockRestraintUtility.WorkTicks, true);
            wait.FailOn(() => !CanForceUnlockNow(pawn, wearer, apparel));
            if (!unlockSelf)
            {
                wait.FailOnCannotTouch(TargetPawnInd, PathEndMode.Touch);
            }

            wait.tickAction = delegate
            {
                woundTickCounter++;
                if (woundTickCounter < ForceUnlockRestraintUtility.WoundCheckIntervalTicks)
                {
                    return;
                }

                woundTickCounter = 0;
                if (Rand.Chance(ForceUnlockRestraintUtility.WoundChancePerInterval))
                {
                    ApplyForceUnlockWound(pawn, wearer);
                }
            };
            yield return wait;

            yield return new Toil
            {
                initAction = delegate
                {
                    if (!CanForceUnlockNow(pawn, wearer, apparel))
                    {
                        MugirlLog.WarningOnce("ForceUnlockSlaveApparel.TargetMissing", "Mugirl.Restraints.UnlockTargetMissingLog".Translate().ToString());
                        return;
                    }

                    CompleteForceUnlock(wearer, apparel);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        private bool CanForceUnlockNow(Pawn actor, Pawn wearer, Apparel apparel)
        {
            return actor != null
                && wearer != null
                && ForceUnlockRestraintUtility.IsLockedForceUnlockable(wearer, apparel)
                && ForceUnlockRestraintUtility.HasRequiredStrength(actor, apparel)
                && (actor == wearer || actor.CanReach(wearer, PathEndMode.Touch, Danger.Some));
        }

        private void CompleteForceUnlock(Pawn wearer, Apparel apparel)
        {
            SlaveApparel slaveApparel = apparel as SlaveApparel;
            if (wearer?.apparel == null || slaveApparel == null)
            {
                return;
            }

            int previousLockCount = slaveApparel.lockCount;
            bool previousLocked = slaveApparel.isLocked;

            slaveApparel.isLocked = false;
            slaveApparel.lockCount = 0;
            wearer.apparel.Unlock(apparel);

            bool dropped = false;
            SlaveApparelAutoStageContext.BeginSuppressNextStage();
            try
            {
                dropped = wearer.apparel.WornApparel.Contains(apparel)
                    && wearer.apparel.TryDrop(apparel, out Apparel _, DropCellFor(wearer), false);
            }
            finally
            {
                SlaveApparelAutoStageContext.EndSuppressNextStage();
            }

            if (dropped)
            {
                Messages.Message("Mugirl.Restraints.ForceUnlock.Success".Translate(pawn.LabelShort, wearer.LabelShort, apparel.Label), wearer, MessageTypeDefOf.PositiveEvent);
                return;
            }

            slaveApparel.isLocked = previousLocked;
            slaveApparel.lockCount = previousLockCount > 0 ? previousLockCount : 1;
            wearer.apparel.Lock(apparel);
        }

        private void ApplyForceUnlockWound(Pawn actor, Pawn wearer)
        {
            if (wearer == null || wearer.Dead)
            {
                return;
            }

            wearer.TakeDamage(new DamageInfo(DamageDefOf.Cut, Rand.Range(2f, 5f), 0f, -1, actor));
            if (wearer.Spawned && wearer.MapHeld != null)
            {
                MoteMaker.ThrowText(wearer.DrawPos, wearer.MapHeld, "Mugirl.Restraints.ForceUnlock.WoundMote".Translate());
            }
        }

        private IntVec3 DropCellFor(Pawn wearer)
        {
            if (wearer != null && wearer.PositionHeld.IsValid)
            {
                return wearer.PositionHeld;
            }

            return pawn.PositionHeld;
        }
    }
}
