using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    // 小孩从雪牛娘处找奶喝：获得哺育进度，并合并普通喝奶收益。
    public class JobDriver_Breastfeed : JobDriver
    {
        private const TargetIndex MooInd = TargetIndex.A;

        private Pawn MooPawn => job.GetTarget(MooInd).Thing as Pawn;
        private Pawn Child => pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn mooPawn = MooPawn;
            return MooGirlMilkInteractionUtility.CanChildBreastfeedNow(Child, mooPawn)
                && pawn.Reserve(mooPawn, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(MooInd);
            this.FailOn(() => !MooGirlMilkInteractionUtility.CanChildBreastfeedNow(Child, MooPawn));

            yield return Toils_Goto.GotoThing(MooInd, PathEndMode.Touch);

            Toil feed = Toils_General.Wait(MooGirlMilkInteractionUtility.ChildBreastfeedInteractionTicks, MooInd);
            feed.WithProgressBarToilDelay(MooInd);
            feed.FailOnCannotTouch(MooInd, PathEndMode.Touch);
            yield return feed;

            yield return Toils_General.DoAtomic(ApplyBreastfeedEffects);
        }

        private void ApplyBreastfeedEffects()
        {
            if (!MooGirlMilkInteractionUtility.ApplyChildBreastfeed(Child, MooPawn))
            {
                pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
            }
        }
    }
}
