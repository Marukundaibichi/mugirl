using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Mugirl
{
    // 小孩从雪牛娘处找奶喝：获得哺育进度，并合并普通喝奶收益。
    public class JobDriver_Breastfeed : JobDriver
    {
        private const TargetIndex MooInd = TargetIndex.A;

        private int forcedWaitJobLoadId = -1;

        private Pawn MooPawn => job.GetTarget(MooInd).Thing as Pawn;
        private Pawn Child => pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn mooPawn = MooPawn;
            return MugirlMilkInteractionUtility.CanChildBreastfeedNow(Child, mooPawn)
                && pawn.Reserve(mooPawn, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(MooInd);
            this.FailOn(() => !MugirlMilkInteractionUtility.CanChildBreastfeedNow(Child, MooPawn));
            this.AddFinishAction(delegate (JobCondition condition)
            {
                MugirlMilkingAnimation.EndFeeding(MooPawn, Child);
                CleanupForcedWait();
            });

            yield return Toils_Goto.GotoThing(MooInd, PathEndMode.Touch);

            Toil feed = Toils_General.Wait(MugirlMilkInteractionUtility.ChildBreastfeedInteractionTicks);
            feed.handlingFacing = true;
            feed.initAction = delegate ()
            {
                Pawn mooPawn = MooPawn;
                if (mooPawn == null || mooPawn.Destroyed)
                {
                    forcedWaitJobLoadId = -1;
                    return;
                }

                pawn.pather.StopDead();
                forcedWaitJobLoadId = MugirlMilkInteractionUtility.ForceMilkInteractionWait(
                    mooPawn,
                    MugirlMilkInteractionUtility.ChildBreastfeedInteractionTicks + 60,
                    Rot4.South);
                MugirlMilkingAnimation.StartFeeding(mooPawn, Child);
            };
            feed.tickAction = delegate ()
            {
                MugirlMilkingAnimation.TickFeeding(MooPawn, Child);
            };
            feed.WithProgressBarToilDelay(MooInd);
            feed.FailOnCannotTouch(MooInd, PathEndMode.Touch);
            yield return feed;

            yield return Toils_General.DoAtomic(ApplyBreastfeedEffects);
        }

        private void ApplyBreastfeedEffects()
        {
            if (!MugirlMilkInteractionUtility.ApplyChildBreastfeed(Child, MooPawn))
            {
                pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
            }
        }

        private void CleanupForcedWait()
        {
            MugirlMilkInteractionUtility.EndMilkInteractionWait(MooPawn, forcedWaitJobLoadId);
            forcedWaitJobLoadId = -1;
        }
    }
}
