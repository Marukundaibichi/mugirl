using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Mugirl
{
    // 小人右键雪牛娘喝奶：补满饱食度 + 心情 + 疗愈 buff
    public class JobDriver_DrinkMilkFromMugirl : JobDriver
    {
        private const TargetIndex MooInd = TargetIndex.A;

        private int forcedWaitJobLoadId = -1;

        private Pawn MooPawn => job.GetTarget(MooInd).Thing as Pawn;
        private Pawn Drinker => pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn mooPawn = MooPawn;
            return MugirlMilkInteractionUtility.CanDrinkMilkNow(Drinker, mooPawn)
                && pawn.Reserve(mooPawn, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(MooInd);
            this.FailOn(() => !MugirlMilkInteractionUtility.CanDrinkMilkNow(Drinker, MooPawn));
            this.AddFinishAction(delegate (JobCondition condition)
            {
                MugirlMilkingAnimation.EndDrinking(Drinker, MooPawn);
                CleanupForcedWait();
            });

            yield return Toils_Goto.GotoThing(MooInd, PathEndMode.Touch);

            Toil drink = Toils_General.Wait(MugirlMilkInteractionUtility.DirectMilkInteractionTicks, MooInd);
            drink.initAction = delegate ()
            {
                Pawn mooPawn = MooPawn;
                if (mooPawn == null || mooPawn.Destroyed)
                {
                    forcedWaitJobLoadId = -1;
                    return;
                }

                pawn.pather.StopDead();
                PawnUtility.ForceWait(mooPawn, MugirlMilkInteractionUtility.DirectMilkInteractionTicks + 60, pawn, true);
                forcedWaitJobLoadId = mooPawn.CurJob != null ? mooPawn.CurJob.loadID : -1;
                MugirlMilkingAnimation.StartDrinking(Drinker, mooPawn);
            };
            drink.tickAction = delegate ()
            {
                MugirlMilkingAnimation.TickDrinking(Drinker, MooPawn);
            };
            drink.WithProgressBarToilDelay(MooInd);
            drink.FailOnCannotTouch(MooInd, PathEndMode.Touch);
            yield return drink;

            yield return Toils_General.DoAtomic(ApplyDrinkEffects);
        }

        private void ApplyDrinkEffects()
        {
            if (!MugirlMilkInteractionUtility.ApplyAdultDrink(Drinker, MooPawn))
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
