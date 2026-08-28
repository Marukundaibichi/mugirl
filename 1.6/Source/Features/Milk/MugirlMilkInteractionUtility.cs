using RimWorld;
using Verse;
using Verse.AI;

namespace Mugirl
{
    internal static class MugirlMilkInteractionUtility
    {
        // 这些是从旧 JobDriver 迁移来的数值锁定常量。
        // 未更新基线并取得确认前不得修改。
        internal const float DirectMilkFoodGain = 0.5f;
        internal const float DirectMilkConsumption = 0.05f;
        internal const float ChildBreastfeedConsumption = 0.3f;
        internal const int DirectMilkInteractionTicks = 400;
        internal const int ChildBreastfeedInteractionTicks = 500;

        internal static bool HasAnyMilk(CompMooMilkable milkComp)
        {
            return milkComp != null && milkComp.Active && milkComp.Fullness > 0f;
        }

        internal static bool HasEnoughMilk(CompMooMilkable milkComp, float percentage)
        {
            return milkComp != null && milkComp.Active && milkComp.Fullness >= percentage;
        }

        internal static bool HasEnoughForDirectMilkInteraction(CompMooMilkable milkComp)
        {
            return HasEnoughMilk(milkComp, DirectMilkConsumption);
        }

        internal static bool HasEnoughForChildBreastfeed(CompMooMilkable milkComp)
        {
            return HasEnoughMilk(milkComp, ChildBreastfeedConsumption);
        }

        internal static int ForceMilkInteractionWait(Pawn pawn, int ticks, Rot4 facing)
        {
            JobDef waitDef = Mugirl_DefOf.Job_MugirlMilkInteractionWait;
            if (pawn == null || pawn.Destroyed || waitDef == null)
            {
                return -1;
            }

            Job waitJob = JobMaker.MakeJob(waitDef);
            waitJob.expiryInterval = ticks;
            waitJob.overrideFacing = facing;
            pawn.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, resumeCurJobAfterwards: true);
            return pawn.CurJob != null ? pawn.CurJob.loadID : -1;
        }

        internal static void EndMilkInteractionWait(Pawn pawn, int expectedLoadId)
        {
            if (pawn != null
                && !pawn.Destroyed
                && pawn.CurJobDef == Mugirl_DefOf.Job_MugirlMilkInteractionWait
                && pawn.CurJob != null
                && pawn.CurJob.loadID == expectedLoadId)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
            }
        }

        internal static bool IsChildMilkSeeker(Pawn pawn)
        {
            return pawn?.RaceProps?.Humanlike == true
                && !pawn.RaceProps.IsMechanoid
                && pawn.ageTracker?.CurLifeStage != null
                && !pawn.ageTracker.CurLifeStage.reproductive;
        }

        internal static bool CanDrinkMilkNow(Pawn drinker, Pawn milkSource)
        {
            if (!CanActAsMilkConsumer(drinker) || milkSource == null || milkSource.Dead)
            {
                return false;
            }

            return HasEnoughForDirectMilkInteraction(milkSource.TryGetComp<CompMooMilkable>());
        }

        internal static bool CanChildBreastfeedNow(Pawn child, Pawn milkSource)
        {
            if (!IsChildMilkSeeker(child) || child.Dead || child.Downed || milkSource == null || milkSource.Dead)
            {
                return false;
            }

            return HasEnoughForChildBreastfeed(milkSource.TryGetComp<CompMooMilkable>());
        }

        internal static bool IsDirectFeedTarget(Pawn target)
        {
            return target != null
                && !target.Dead
                && target.RaceProps?.Humanlike == true
                && (target.Downed || target.IsColonist);
        }

        internal static bool CanFeedPawnNow(Pawn feeder, Pawn target)
        {
            if (!CanActAsMilkConsumer(feeder) || feeder == target || !IsDirectFeedTarget(target))
            {
                return false;
            }

            return HasEnoughForDirectMilkInteraction(feeder.TryGetComp<CompMooMilkable>());
        }

        internal static bool ApplyAdultDrink(Pawn drinker, Pawn milkSource)
        {
            if (!CanDrinkMilkNow(drinker, milkSource) || !TryConsumeMilk(milkSource, DirectMilkConsumption))
            {
                return false;
            }

            AddFood(drinker, DirectMilkFoodGain);
            GainMemory(drinker, MugirlRequiredDefs.Thoughts.MugirlDrankMilk);
            GainMemory(drinker, MugirlRequiredDefs.Thoughts.ConsumedMugirlMilk);

            if (milkSource?.Downed == true)
            {
                AddHealingIfMissing(drinker, MugirlRequiredDefs.Hediffs.MugirlMilkHealing);
            }

            return true;
        }

        internal static bool ApplyFeed(Pawn target, Pawn milkSource)
        {
            if (!CanFeedPawnNow(milkSource, target) || !TryConsumeMilk(milkSource, DirectMilkConsumption))
            {
                return false;
            }

            AddHealingIfMissing(target, MugirlRequiredDefs.Hediffs.MugirlMilkHealing);
            GainMemory(target, MugirlRequiredDefs.Thoughts.ConsumedMugirlMilk);
            GainMemory(target, MugirlRequiredDefs.Thoughts.MugirlFedMilk);
            AddFood(target, DirectMilkFoodGain);
            return true;
        }

        internal static bool ApplyChildBreastfeed(Pawn child, Pawn milkSource)
        {
            if (!CanChildBreastfeedNow(child, milkSource) || !TryConsumeMilk(milkSource, ChildBreastfeedConsumption))
            {
                return false;
            }

            AddFood(child, DirectMilkFoodGain);
            GainMemory(child, MugirlOptionalDefs.ThoughtDefs.MugirlDrankMilk);
            GainMemory(child, MugirlOptionalDefs.ThoughtDefs.ConsumedMugirlMilk);
            MugirlNurtureUtility.AddChildNurtureProgress(child, milkSource);
            return true;
        }

        private static bool CanActAsMilkConsumer(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Downed && pawn.RaceProps?.Humanlike == true && !pawn.RaceProps.IsMechanoid;
        }

        private static void AddFood(Pawn pawn, float amount)
        {
            if (pawn?.needs?.food != null)
            {
                pawn.needs.food.CurLevel += amount;
            }
        }

        private static void GainMemory(Pawn pawn, ThoughtDef thoughtDef)
        {
            if (thoughtDef != null)
            {
                pawn?.needs?.mood?.thoughts?.memories?.TryGainMemory(thoughtDef);
            }
        }

        private static void AddHealingIfMissing(Pawn pawn, HediffDef hediffDef)
        {
            if (pawn?.health?.hediffSet != null && hediffDef != null && !pawn.health.hediffSet.HasHediff(hediffDef))
            {
                pawn.health.AddHediff(hediffDef);
            }
        }

        private static bool TryConsumeMilk(Pawn milkSource, float percentage)
        {
            CompMooMilkable comp = milkSource?.TryGetComp<CompMooMilkable>();
            return HasEnoughMilk(comp, percentage) && comp.ConsumePercentage(percentage);
        }
    }
}
