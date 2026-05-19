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

            Toil drink = Toils_General.Wait(400, MooInd);
            drink.WithProgressBarToilDelay(MooInd);
            drink.FailOnCannotTouch(MooInd, PathEndMode.Touch);
            yield return drink;

            yield return Toils_General.DoAtomic(ApplyDrinkEffects);
        }

        private void ApplyDrinkEffects()
        {
            // 补满饱食度
            if (Drinker.needs?.food != null)
            {
                Drinker.needs.food.CurLevel += 0.5f;
            }

            // 添加喝奶心情（+12 mood）
            ThoughtDef drinkThought = DefDatabase<ThoughtDef>.GetNamed("MooGirl_DrankMilk");
            if (drinkThought != null)
            {
                Drinker.needs.mood?.thoughts?.memories?.TryGainMemory(drinkThought);
            }

            // 添加 Consumed_MooGirlMilk 想法（+8 mood，自动添加疗愈 hediff）
            ThoughtDef milkThought = DefDatabase<ThoughtDef>.GetNamed("Consumed_MooGirlMilk");
            if (milkThought != null)
            {
                Drinker.needs.mood?.thoughts?.memories?.TryGainMemory(milkThought);
            }

            // 如果雪牛娘倒地，额外添加强化疗愈效果
            if (MooPawn.Downed)
            {
                HediffDef healingDef = DefDatabase<HediffDef>.GetNamed("MooGirl_MilkHealing");
                if (healingDef != null && !Drinker.health.hediffSet.HasHediff(healingDef))
                {
                    Drinker.health.AddHediff(healingDef);
                }
            }

            // 消耗雪牛娘 5% 奶量
            CompMooMilkable comp = MooPawn.TryGetComp<CompMooMilkable>();
            comp?.ConsumePercentage(0.05f);
        }
    }
}
