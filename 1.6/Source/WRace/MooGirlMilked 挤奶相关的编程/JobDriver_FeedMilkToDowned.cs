using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    // 雪牛娘去喂倒地的小人：给倒地者疗愈 buff + 心情
    public class JobDriver_FeedMilkToDowned : JobDriver
    {
        private const TargetIndex DownedInd = TargetIndex.A;

        private Pawn DownedPawn => (Pawn)job.GetTarget(DownedInd).Thing;
        private Pawn MooPawn => pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return MooGirlMilkInteractionUtility.CanFeedDownedPawnNow(MooPawn, DownedPawn) && pawn.Reserve(DownedPawn, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(DownedInd);
            this.FailOn(() => !MooGirlMilkInteractionUtility.CanFeedDownedPawnNow(MooPawn, DownedPawn));

            yield return Toils_Goto.GotoThing(DownedInd, PathEndMode.Touch);

            Toil feed = Toils_General.Wait(MooGirlMilkInteractionUtility.DirectMilkInteractionTicks, DownedInd);
            feed.WithProgressBarToilDelay(DownedInd);
            feed.FailOnCannotTouch(DownedInd, PathEndMode.Touch);
            yield return feed;

            yield return Toils_General.DoAtomic(ApplyFeedEffects);
        }

        private void ApplyFeedEffects()
        {
            MooGirlMilkInteractionUtility.ApplyDownedFeed(DownedPawn, MooPawn);
        }
    }
}
