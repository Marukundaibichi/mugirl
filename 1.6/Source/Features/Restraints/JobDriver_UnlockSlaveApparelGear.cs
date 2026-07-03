using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public class JobDriver_UnlockSlaveApparelGear : JobDriver
    {
        protected TargetIndex iitem = TargetIndex.A;
        protected TargetIndex itar = TargetIndex.B;
        protected TargetIndex appear = TargetIndex.C;

        protected Thing item
        {
            get
            {
                var target = job.GetTarget(iitem);
                if (!target.HasThing)
                    return null;
                return target.Thing;
            }
        }

        protected Thing targetThing
        {
            get
            {
                var target = job.GetTarget(itar);
                if (!target.HasThing)
                    return null;
                return target.Thing;
            }
        }

        protected Pawn targetPawn
        {
            get
            {
                Thing target = targetThing;
                if (target is Pawn pawnTarget)
                    return pawnTarget;
                if (target is Corpse corpse)
                    return corpse.InnerPawn;
                return null;
            }
        }

        protected Apparel targetApparel
        {
            get
            {
                if (!job.GetTarget(appear).HasThing)
                    return null;
                return job.GetTarget(appear).Thing as Apparel;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Thing itemToUse = item;
            Thing targetToReserve = targetThing;
            Apparel apparelToUnlock = targetApparel;
            if (itemToUse == null || targetToReserve == null || apparelToUnlock == null)
            {
                return false;
            }

            bool reserveTarget = targetToReserve == pawn || pawn.Reserve(targetToReserve, job, 1, -1, null, errorOnFailed);
            bool reserveApparel = pawn.Reserve(apparelToUnlock, job, 1, -1, null, errorOnFailed);
            bool itemAlreadyHeld = pawn.inventory != null && pawn.inventory.Contains(itemToUse);
            bool reserveItem = itemAlreadyHeld || pawn.Reserve(itemToUse, job, 1, -1, null, errorOnFailed);

            return reserveTarget && reserveApparel && reserveItem;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Thing itemToUse = item;
            Thing targetToReserve = targetThing;
            Pawn pawnToUnlock = targetPawn;
            Apparel apparelToUnlock = targetApparel;
            if (itemToUse == null || targetToReserve == null || pawnToUnlock?.apparel == null || apparelToUnlock == null)
            {
                yield break;
            }

            SlaveApparel apparel = apparelToUnlock as SlaveApparel;
            if (apparel == null)
            {
                yield break;
            }

            if (!apparelToUnlock.SatisfiesKey(itemToUse) || !TargetStillWearsApparel(pawnToUnlock, apparelToUnlock))
            {
                yield break;
            }

            if (targetToReserve != pawn)
            {
                yield return Toils_Reserve.Reserve(itar);
            }

            yield return Toils_Reserve.Reserve(appear);

            if ((pawn.inventory != null) && pawn.inventory.Contains(itemToUse))
            {
                yield return Toils_Misc.TakeItemFromInventoryToCarrier(pawn, iitem);
            }
            else if (itemToUse.Spawned)
            {
                yield return Toils_Reserve.Reserve(iitem);
                yield return Toils_Goto.GotoThing(iitem, PathEndMode.ClosestTouch).FailOnForbidden(iitem);
                yield return new Toil
                {
                    initAction = () =>
                    {
                        if (itemToUse == null || itemToUse.Destroyed || pawn.carryTracker.TryStartCarry(itemToUse, 1) <= 0)
                        {
                            pawn.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
                        }
                    },
                    defaultCompleteMode = ToilCompleteMode.Instant
                };
            }
            else
            {
                yield break;
            }

            if (targetToReserve != pawn)
            {
                yield return Toils_Goto.GotoThing(itar, PathEndMode.Touch);
            }

            Toil wait = Toils_General.WaitWith(itar, apparel.SlaveDef.unlockTick, true);
            wait.FailOn(() => !TargetStillWearsApparel(pawnToUnlock, apparelToUnlock));
            if (targetToReserve != pawn)
            {
                wait.FailOnCannotTouch(itar, PathEndMode.Touch);
            }
            yield return wait;

            yield return new Toil
            {
                initAction = () =>
                {
                    if (!TargetStillWearsApparel(pawnToUnlock, apparelToUnlock))
                    {
                        MugirlLog.WarningOnce("UnlockSlaveApparel.TargetMissing", "Mugirl.Restraints.UnlockTargetMissingLog".Translate().ToString());
                        return;
                    }

                    int previousLockCount = apparel.lockCount;
                    apparel.lockCount--;
                    if (apparel.lockCount <= 0)
                    {
                        apparel.isLocked = false;
                        apparel.lockCount = 0;

                        if (pawnToUnlock.apparel.WornApparel.Contains(apparelToUnlock))
                        {
                            if (pawnToUnlock.apparel.TryDrop(apparelToUnlock, out Apparel _, DropCellFor(targetToReserve, pawnToUnlock), false))
                            {
                                Messages.Message("Mugirl.SlaveApparelFullyUnlocked".Translate(pawnToUnlock.LabelShort), pawnToUnlock, MessageTypeDefOf.PositiveEvent);
                            }
                            else
                            {
                                apparel.isLocked = true;
                                apparel.lockCount = previousLockCount > 0 ? previousLockCount : 1;
                                pawnToUnlock.apparel.Lock(apparel);
                                return;
                            }
                        }
                    }
                    else
                    {
                        Messages.Message("Mugirl.SlaveApparelUnlockPartial".Translate(pawn.LabelShort, pawnToUnlock.LabelShort, apparel.lockCount), pawnToUnlock, MessageTypeDefOf.PositiveEvent);
                    }

                    if (itemToUse != null && !itemToUse.Destroyed)
                        itemToUse.Destroy();
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        private static bool TargetStillWearsApparel(Pawn target, Apparel apparel)
        {
            return target?.apparel != null && apparel != null && target.apparel.WornApparel.Contains(apparel);
        }

        private IntVec3 DropCellFor(Thing target, Pawn wearer)
        {
            if (target != null && target.PositionHeld.IsValid)
            {
                return target.PositionHeld;
            }

            if (wearer != null && wearer.PositionHeld.IsValid)
            {
                return wearer.PositionHeld;
            }

            return pawn.PositionHeld;
        }
    }
}
