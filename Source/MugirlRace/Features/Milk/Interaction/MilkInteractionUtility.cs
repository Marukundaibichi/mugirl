using System.Diagnostics.CodeAnalysis;
using Verse.AI;

namespace Mugirl.Features.Milk;

// 喝奶、喂奶、小孩找奶喝的判定规则与效果结算，由右键菜单和三个互动 JobDriver 共用。
internal static class MilkInteractionUtility
{
    // 这些是从旧 JobDriver 迁移来的数值锁定常量；未更新基线并取得确认前不得修改。
    internal const float DirectMilkFoodGain = 0.5f;
    internal const float DirectMilkConsumption = 0.05f;
    internal const float ChildBreastfeedConsumption = 0.3f;
    internal const int DirectMilkInteractionTicks = 400;
    internal const int ChildBreastfeedInteractionTicks = 500;

    // 互动对方的等待时长比互动本身多 1 秒，正常情况下由发起方结束时提前解除。
    internal const int PartnerWaitExtraTicks = 60;

    internal static bool HasAnyMilk([NotNullWhen(true)] CompMilkProduction? milkComp)
    {
        return milkComp != null && milkComp.Active && milkComp.Fullness > 0f;
    }

    internal static bool HasEnoughMilk([NotNullWhen(true)] CompMilkProduction? milkComp, float percentage)
    {
        return milkComp != null && milkComp.Active && milkComp.Fullness >= percentage;
    }

    internal static bool HasEnoughForDirectMilkInteraction([NotNullWhen(true)] CompMilkProduction? milkComp)
    {
        return HasEnoughMilk(milkComp, DirectMilkConsumption);
    }

    internal static bool HasEnoughForChildBreastfeed([NotNullWhen(true)] CompMilkProduction? milkComp)
    {
        return HasEnoughMilk(milkComp, ChildBreastfeedConsumption);
    }

    // 让互动对方原地等待（Job_MugirlMilkInteractionWait），返回等待 Job 的 loadID；对方无效时返回 NoJob。
    internal static int ForceMilkInteractionWait(Pawn? pawn, int ticks, Rot4 facing)
    {
        if (pawn == null || pawn.Destroyed)
        {
            return MilkPartnerWait.NoJob;
        }

        Job waitJob = JobMaker.MakeJob(MilkDefOf.Mugirl_MilkInteractionWait);
        waitJob.expiryInterval = ticks;
        waitJob.overrideFacing = facing;
        return MilkPartnerWait.Begin(pawn, waitJob);
    }

    internal static void EndMilkInteractionWait(Pawn? pawn, int expectedLoadId)
    {
        MilkPartnerWait.End(pawn, MilkDefOf.Mugirl_MilkInteractionWait, expectedLoadId);
    }

    internal static bool IsChildMilkSeeker([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn?.RaceProps?.Humanlike == true
            && !pawn.RaceProps.IsMechanoid
            && pawn.ageTracker?.CurLifeStage != null
            && !pawn.ageTracker.CurLifeStage.reproductive;
    }

    internal static bool CanDrinkMilkNow([NotNullWhen(true)] Pawn? drinker, [NotNullWhen(true)] Pawn? milkSource)
    {
        return CanActAsMilkConsumer(drinker)
            && milkSource != null
            && !milkSource.Dead
            && HasEnoughForDirectMilkInteraction(milkSource.TryGetComp<CompMilkProduction>());
    }

    internal static bool CanChildBreastfeedNow([NotNullWhen(true)] Pawn? child, [NotNullWhen(true)] Pawn? milkSource)
    {
        return IsChildMilkSeeker(child)
            && !child.Dead
            && !child.Downed
            && milkSource != null
            && !milkSource.Dead
            && HasEnoughForChildBreastfeed(milkSource.TryGetComp<CompMilkProduction>());
    }

    internal static bool IsDirectFeedTarget([NotNullWhen(true)] Pawn? target)
    {
        return target != null
            && !target.Dead
            && target.RaceProps?.Humanlike == true
            && (target.Downed || target.IsColonist);
    }

    internal static bool CanFeedPawnNow([NotNullWhen(true)] Pawn? feeder, [NotNullWhen(true)] Pawn? target)
    {
        return CanActAsMilkConsumer(feeder)
            && feeder != target
            && IsDirectFeedTarget(target)
            && HasEnoughForDirectMilkInteraction(feeder.TryGetComp<CompMilkProduction>());
    }

    internal static bool ApplyAdultDrink(Pawn drinker, Pawn? milkSource)
    {
        if (!CanDrinkMilkNow(drinker, milkSource) || !TryConsumeMilk(milkSource, DirectMilkConsumption))
        {
            return false;
        }

        AddFood(drinker, DirectMilkFoodGain);
        GainMemory(drinker, MilkDefOf.Mugirl_DrankMilk);
        GainMemory(drinker, MilkDefOf.Mugirl_ConsumedMilk);

        if (milkSource?.Downed == true)
        {
            AddHealingIfMissing(drinker, MilkDefOf.Mugirl_MilkHealing);
        }

        return true;
    }

    internal static bool ApplyFeed(Pawn? target, Pawn milkSource)
    {
        if (!CanFeedPawnNow(milkSource, target) || !TryConsumeMilk(milkSource, DirectMilkConsumption))
        {
            return false;
        }

        AddHealingIfMissing(target, MilkDefOf.Mugirl_MilkHealing);
        GainMemory(target, MilkDefOf.Mugirl_ConsumedMilk);
        GainMemory(target, MilkDefOf.Mugirl_FedMilk);
        AddFood(target, DirectMilkFoodGain);
        return true;
    }

    internal static bool ApplyChildBreastfeed(Pawn child, Pawn? milkSource)
    {
        if (!CanChildBreastfeedNow(child, milkSource) || !TryConsumeMilk(milkSource, ChildBreastfeedConsumption))
        {
            return false;
        }

        AddFood(child, DirectMilkFoodGain);
        GainMemory(child, MilkDefOf.Mugirl_DrankMilk);
        GainMemory(child, MilkDefOf.Mugirl_ConsumedMilk);
        NurtureUtility.AddChildNurtureProgress(child, milkSource);
        return true;
    }

    private static bool CanActAsMilkConsumer([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn != null && !pawn.Dead && !pawn.Downed && pawn.RaceProps?.Humanlike == true && !pawn.RaceProps.IsMechanoid;
    }

    private static void AddFood(Pawn pawn, float amount)
    {
        Need_Food? food = pawn.needs?.food;
        if (food != null)
        {
            food.CurLevel += amount;
        }
    }

    private static void GainMemory(Pawn pawn, ThoughtDef thoughtDef)
    {
        pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(thoughtDef);
    }

    private static void AddHealingIfMissing(Pawn pawn, HediffDef hediffDef)
    {
        if (pawn.health?.hediffSet != null && !pawn.health.hediffSet.HasHediff(hediffDef))
        {
            pawn.health.AddHediff(hediffDef);
        }
    }

    private static bool TryConsumeMilk(Pawn? milkSource, float percentage)
    {
        CompMilkProduction? comp = milkSource?.TryGetComp<CompMilkProduction>();
        return HasEnoughMilk(comp, percentage) && comp.ConsumePercentage(percentage);
    }
}
