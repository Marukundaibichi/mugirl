using RimWorld;
using Verse;

namespace MooGirl
{
    internal static class MooGirlMilkInteractionUtility
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

        internal static bool CanFeedDownedPawnNow(Pawn feeder, Pawn target)
        {
            if (!CanActAsMilkConsumer(feeder) || target == null || target.Dead || !target.Downed)
            {
                return false;
            }

            if (target.RaceProps?.Humanlike != true)
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
            GainMemory(drinker, MooGirlRequiredDefs.Thoughts.MooGirlDrankMilk);
            GainMemory(drinker, MooGirlRequiredDefs.Thoughts.ConsumedMooGirlMilk);

            if (milkSource?.Downed == true)
            {
                AddHealingIfMissing(drinker, MooGirlRequiredDefs.Hediffs.MooGirlMilkHealing);
            }

            return true;
        }

        internal static bool ApplyDownedFeed(Pawn target, Pawn milkSource)
        {
            if (!CanFeedDownedPawnNow(milkSource, target) || !TryConsumeMilk(milkSource, DirectMilkConsumption))
            {
                return false;
            }

            AddHealingIfMissing(target, MooGirlRequiredDefs.Hediffs.MooGirlMilkHealing);
            GainMemory(target, MooGirlRequiredDefs.Thoughts.ConsumedMooGirlMilk);
            GainMemory(target, MooGirlRequiredDefs.Thoughts.MooGirlFedMilk);
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
            GainMemory(child, MooGirlOptionalDefs.ThoughtDefs.MooGirlDrankMilk);
            GainMemory(child, MooGirlOptionalDefs.ThoughtDefs.ConsumedMooGirlMilk);
            MooGirlNurtureUtility.AddChildNurtureProgress(child, milkSource);
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
