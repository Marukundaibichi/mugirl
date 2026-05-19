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
        private const float MinMilkFullness = 0.05f;

        private Pawn DownedPawn => (Pawn)job.GetTarget(DownedInd).Thing;
        private Pawn MooPawn => pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return CanFeedMilkNow(MooPawn, DownedPawn) && pawn.Reserve(DownedPawn, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(DownedInd);
            this.FailOn(() => !CanFeedMilkNow(MooPawn, DownedPawn));

            yield return Toils_Goto.GotoThing(DownedInd, PathEndMode.Touch);

            Toil feed = Toils_General.Wait(400, DownedInd);
            feed.WithProgressBarToilDelay(DownedInd);
            feed.FailOnCannotTouch(DownedInd, PathEndMode.Touch);
            yield return feed;

            yield return Toils_General.DoAtomic(ApplyFeedEffects);
        }

        private void ApplyFeedEffects()
        {
            // 给倒地者添加强力疗愈 buff
            HediffDef healingDef = DefDatabase<HediffDef>.GetNamed("MooGirl_MilkHealing");
            if (healingDef != null && !DownedPawn.health.hediffSet.HasHediff(healingDef))
            {
                DownedPawn.health.AddHediff(healingDef);
            }

            // 添加 Consumed_MooGirlMilk 想法（自动添加疗愈 hediff）
            ThoughtDef milkThought = DefDatabase<ThoughtDef>.GetNamed("Consumed_MooGirlMilk");
            if (milkThought != null)
            {
                DownedPawn.needs.mood?.thoughts?.memories?.TryGainMemory(milkThought);
            }

            // 添加被喂奶心情
            ThoughtDef fedThought = DefDatabase<ThoughtDef>.GetNamed("MooGirl_FedMilk");
            if (fedThought != null)
            {
                DownedPawn.needs.mood?.thoughts?.memories?.TryGainMemory(fedThought);
            }

            // 补饱食度
            if (DownedPawn.needs?.food != null)
            {
                DownedPawn.needs.food.CurLevel += 0.5f;
            }

            // 消耗雪牛娘 5% 奶量
            CompMooMilkable comp = MooPawn.TryGetComp<CompMooMilkable>();
            comp?.ConsumePercentage(0.05f);
        }

        private static bool CanFeedMilkNow(Pawn feeder, Pawn target)
        {
            if (feeder == null || target == null || feeder.Dead || feeder.Downed || target.Dead || !target.Downed)
            {
                return false;
            }

            if (target.RaceProps?.Humanlike != true)
            {
                return false;
            }

            CompMooMilkable comp = feeder.TryGetComp<CompMooMilkable>();
            return comp != null && comp.Active && comp.Fullness > MinMilkFullness;
        }
    }
}
