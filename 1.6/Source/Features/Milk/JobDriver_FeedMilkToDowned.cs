using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Mugirl
{
    // 兼容旧 JobDef 名称：雪牛娘可给倒地小人或站立殖民者喂奶。
    public class JobDriver_FeedMilkToDowned : JobDriver
    {
        private const TargetIndex RecipientInd = TargetIndex.A;

        private int forcedWaitJobLoadId = -1;

        private Pawn Recipient => job.GetTarget(RecipientInd).Thing as Pawn;
        private Pawn MooPawn => pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn recipient = Recipient;
            return MugirlMilkInteractionUtility.CanFeedPawnNow(MooPawn, recipient)
                && pawn.Reserve(recipient, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(RecipientInd);
            this.FailOn(() => !MugirlMilkInteractionUtility.CanFeedPawnNow(MooPawn, Recipient));
            this.AddFinishAction(delegate (JobCondition condition)
            {
                MugirlMilkingAnimation.EndFeeding(MooPawn, Recipient);
                CleanupForcedWait();
            });

            yield return Toils_Goto.GotoThing(RecipientInd, PathEndMode.Touch);

            Toil feed = Toils_General.Wait(MugirlMilkInteractionUtility.DirectMilkInteractionTicks);
            feed.handlingFacing = true;
            feed.initAction = delegate ()
            {
                Pawn recipient = Recipient;
                pawn.pather.StopDead();
                if (recipient != null && !recipient.Destroyed && !recipient.Downed)
                {
                    forcedWaitJobLoadId = MugirlMilkInteractionUtility.ForceMilkInteractionWait(
                        recipient,
                        MugirlMilkInteractionUtility.DirectMilkInteractionTicks + 60,
                        MugirlMilkingAnimation.FeedingRecipientFacing(MooPawn, recipient));
                }
                else
                {
                    forcedWaitJobLoadId = -1;
                }

                MugirlMilkingAnimation.StartFeeding(MooPawn, recipient);
            };
            feed.tickAction = delegate ()
            {
                MugirlMilkingAnimation.TickFeeding(MooPawn, Recipient);
            };
            feed.WithProgressBarToilDelay(RecipientInd);
            feed.FailOnCannotTouch(RecipientInd, PathEndMode.Touch);
            yield return feed;

            yield return Toils_General.DoAtomic(ApplyFeedEffects);
        }

        private void ApplyFeedEffects()
        {
            if (!MugirlMilkInteractionUtility.ApplyFeed(Recipient, MooPawn))
            {
                pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
            }
        }

        private void CleanupForcedWait()
        {
            MugirlMilkInteractionUtility.EndMilkInteractionWait(Recipient, forcedWaitJobLoadId);
            forcedWaitJobLoadId = -1;
        }
    }
}
