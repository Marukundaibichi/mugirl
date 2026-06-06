using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
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
                if (target == null || !target.HasThing)
                    return null;
                return target.Thing;
            }
        }

        protected Pawn targetPawn
        {
            get { return base.job.GetTarget(itar).Thing as Pawn; }
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
            // 修改1：自己解锁自己时，跳过重复预占位
            bool reservePawn = targetPawn != pawn ? pawn.Reserve(targetPawn, job, 1, -1, null, errorOnFailed) : true;

            return reservePawn &&
                    (targetApparel != null ? pawn.Reserve(targetApparel, job, 1, -1, null, errorOnFailed) : true);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Thing itemToUse = null;
            if (job.GetTarget(iitem).HasThing)
            {
                itemToUse = job.GetTarget(iitem).Thing;
            }
            // 修改2：增加空检查，防止 NullReferenceException
            if (targetPawn == null)
            {
                yield break;
            }
            if (targetApparel == null)
            {
                yield break;
            }

            SlaveApparel apparel = targetApparel as SlaveApparel;
            if (apparel == null)
            {
                yield break;
            }

            yield return Toils_Reserve.Reserve(itar);

            // 修改3：处理物品，如果 pawn 自己拥有则跳过拿取
            if ((pawn.inventory != null) && pawn.inventory.Contains(item))
            {
                yield return Toils_Misc.TakeItemFromInventoryToCarrier(pawn, iitem);
            }
            else if (item != null)
            {
                yield return Toils_Reserve.Reserve(iitem);
                yield return Toils_Goto.GotoThing(iitem, PathEndMode.ClosestTouch).FailOnForbidden(iitem);
                yield return new Toil
                {
                    initAction = () =>
                    {
                        pawn.carryTracker.TryStartCarry(item, 1);
                    },
                    defaultCompleteMode = ToilCompleteMode.Instant
                };
            }

            yield return Toils_Goto.GotoThing(itar, PathEndMode.Touch);

            yield return Toils_General.WaitWith(itar, apparel.SlaveDef.unlockTick, true);

            yield return new Toil
            {
                initAction = () =>
                {
                    // 修改4：安全检查
            if (targetPawn == null || targetApparel == null)
            {
                MooGirlLog.Warning("MooGirl.Restraints.UnlockTargetMissingLog".Translate().ToString());
                return;
            }

                    apparel.lockCount--;
                    if (apparel.lockCount <= 0)
                    {
                        // 修改5：自己解锁自己也可以正常移除
                        Apparel apparelToUnlock = targetApparel;
                        Pawn pawnToUnlock = targetPawn;

                        if (pawnToUnlock != null && apparelToUnlock != null && pawnToUnlock.apparel.WornApparel.Contains(apparelToUnlock))
                        {
                            pawnToUnlock.apparel.Remove(apparelToUnlock);
                            GenPlace.TryPlaceThing(apparelToUnlock, pawnToUnlock.Position, pawnToUnlock.Map, ThingPlaceMode.Near);
                            Messages.Message("MooGirl.SlaveApparelFullyUnlocked".Translate(pawnToUnlock.LabelShort), pawnToUnlock, MessageTypeDefOf.PositiveEvent);
                        }
                    }
                    else
                    {
                        Messages.Message("MooGirl.SlaveApparelUnlockPartial".Translate(pawn.LabelShort, targetPawn.LabelShort, apparel.lockCount), targetPawn, MessageTypeDefOf.PositiveEvent);
                    }

                    if (itemToUse != null)
                        itemToUse.Destroy();
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
