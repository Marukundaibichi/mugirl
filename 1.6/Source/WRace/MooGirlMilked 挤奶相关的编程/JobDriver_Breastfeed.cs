using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    // 小孩从雪牛娘处喝母乳：获得学习/生长 buff，消耗牛娘奶量
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
            // 给小孩添加母乳喂养 buff
            HediffDef fedDef = DefDatabase<HediffDef>.GetNamed("MooGirl_Breastfed");
            if (fedDef != null && !Child.health.hediffSet.HasHediff(fedDef))
            {
                Child.health.AddHediff(fedDef);
            }

            // 消耗雪牛娘 30% 奶量
            CompMooMilkable comp = MooPawn.TryGetComp<CompMooMilkable>();
            comp?.ConsumePercentage(0.3f);
        }
    }
}
