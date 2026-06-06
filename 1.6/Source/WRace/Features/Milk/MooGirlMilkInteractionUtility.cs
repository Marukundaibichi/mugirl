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

        internal static bool HasEnoughForDirectMilkInteraction(CompMooMilkable milkComp)
        {
            return milkComp != null && milkComp.Active && milkComp.Fullness > DirectMilkConsumption;
        }

        internal static bool CanFeedDownedPawnNow(Pawn feeder, Pawn target)
        {
            if (feeder == null || target == null || feeder.Dead || feeder.Downed || target.Dead || !target.Downed)
            {
                return false;
            }

            if (target.RaceProps?.Humanlike != true)
            {
                return false;
            }

            return HasEnoughForDirectMilkInteraction(feeder.TryGetComp<CompMooMilkable>());
        }

        internal static void ApplyAdultDrink(Pawn drinker, Pawn milkSource)
        {
            AddFood(drinker, DirectMilkFoodGain);
            GainMemory(drinker, MooGirlRequiredDefs.Thoughts.MooGirlDrankMilk);
            GainMemory(drinker, MooGirlRequiredDefs.Thoughts.ConsumedMooGirlMilk);

            if (milkSource?.Downed == true)
            {
                AddHealingIfMissing(drinker, MooGirlRequiredDefs.Hediffs.MooGirlMilkHealing);
            }

            ConsumeMilk(milkSource, DirectMilkConsumption);
        }

        internal static void ApplyDownedFeed(Pawn target, Pawn milkSource)
        {
            AddHealingIfMissing(target, MooGirlRequiredDefs.Hediffs.MooGirlMilkHealing);
            GainMemory(target, MooGirlRequiredDefs.Thoughts.ConsumedMooGirlMilk);
            GainMemory(target, MooGirlRequiredDefs.Thoughts.MooGirlFedMilk);
            AddFood(target, DirectMilkFoodGain);
            ConsumeMilk(milkSource, DirectMilkConsumption);
        }

        internal static void ApplyChildBreastfeed(Pawn child, Pawn milkSource)
        {
            AddFood(child, DirectMilkFoodGain);
            GainMemory(child, MooGirlOptionalDefs.ThoughtDefs.MooGirlDrankMilk);
            GainMemory(child, MooGirlOptionalDefs.ThoughtDefs.ConsumedMooGirlMilk);
            MooGirlNurtureUtility.AddChildNurtureProgress(child, milkSource);
            ConsumeMilk(milkSource, ChildBreastfeedConsumption);
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

        private static void ConsumeMilk(Pawn milkSource, float percentage)
        {
            milkSource?.TryGetComp<CompMooMilkable>()?.ConsumePercentage(percentage);
        }
    }
}
