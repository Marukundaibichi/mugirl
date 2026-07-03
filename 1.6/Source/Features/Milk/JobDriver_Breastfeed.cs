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
                CleanupForcedWait();
            });

            yield return Toils_Goto.GotoThing(MooInd, PathEndMode.Touch);

            Toil feed = Toils_General.Wait(MugirlMilkInteractionUtility.ChildBreastfeedInteractionTicks, MooInd);
            feed.initAction = delegate ()
            {
                Pawn mooPawn = MooPawn;
                if (mooPawn == null || mooPawn.Destroyed)
                {
                    forcedWaitJobLoadId = -1;
                    return;
                }

                pawn.pather.StopDead();
                PawnUtility.ForceWait(mooPawn, MugirlMilkInteractionUtility.ChildBreastfeedInteractionTicks + 60, pawn, true);
                forcedWaitJobLoadId = mooPawn.CurJob != null ? mooPawn.CurJob.loadID : -1;
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
            Pawn mooPawn = MooPawn;
            if (mooPawn != null
                && !mooPawn.Destroyed
                && mooPawn.CurJobDef == JobDefOf.Wait_MaintainPosture
                && mooPawn.CurJob != null
                && mooPawn.CurJob.loadID == forcedWaitJobLoadId)
            {
                mooPawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
            }

            forcedWaitJobLoadId = -1;
        }
    }
}
