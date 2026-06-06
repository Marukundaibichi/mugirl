using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    // 小人右键雪牛娘喝奶：补满饱食度 + 心情 + 疗愈 buff
    public class JobDriver_DrinkMilkFromMooGirl : JobDriver
    {
        private const TargetIndex MooInd = TargetIndex.A;

        private Pawn MooPawn => (Pawn)job.GetTarget(MooInd).Thing;
        private Pawn Drinker => pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(MooPawn, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(MooInd);

            yield return Toils_Goto.GotoThing(MooInd, PathEndMode.Touch);

            Toil drink = Toils_General.Wait(MooGirlMilkInteractionUtility.DirectMilkInteractionTicks, MooInd);
            drink.WithProgressBarToilDelay(MooInd);
            drink.FailOnCannotTouch(MooInd, PathEndMode.Touch);
            yield return drink;

            yield return Toils_General.DoAtomic(ApplyDrinkEffects);
        }

        private void ApplyDrinkEffects()
        {
            MooGirlMilkInteractionUtility.ApplyAdultDrink(Drinker, MooPawn);
        }
    }
}
