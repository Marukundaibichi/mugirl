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

        private Pawn MooPawn => (Pawn)job.GetTarget(MooInd).Thing;
        private Pawn Child => pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(MooPawn, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(MooInd);

            yield return Toils_Goto.GotoThing(MooInd, PathEndMode.Touch);

            Toil feed = Toils_General.Wait(500, MooInd);
            feed.WithProgressBarToilDelay(MooInd);
            feed.FailOnCannotTouch(MooInd, PathEndMode.Touch);
            yield return feed;

            yield return Toils_General.DoAtomic(ApplyBreastfeedEffects);
        }

        private void ApplyBreastfeedEffects()
        {
            if (Child.needs?.food != null)
            {
                Child.needs.food.CurLevel += 0.5f;
            }

            ThoughtDef drinkThought = DefDatabase<ThoughtDef>.GetNamedSilentFail("MooGirl_DrankMilk");
            if (drinkThought != null)
            {
                Child.needs.mood?.thoughts?.memories?.TryGainMemory(drinkThought);
            }

            ThoughtDef milkThought = DefDatabase<ThoughtDef>.GetNamedSilentFail("Consumed_MooGirlMilk");
            if (milkThought != null)
            {
                Child.needs.mood?.thoughts?.memories?.TryGainMemory(milkThought);
            }

            MooGirlNurtureUtility.AddChildNurtureProgress(Child, MooPawn);

            // 消耗雪牛娘 30% 奶量
            CompMooMilkable comp = MooPawn.TryGetComp<CompMooMilkable>();
            comp?.ConsumePercentage(0.3f);
        }
    }
}
