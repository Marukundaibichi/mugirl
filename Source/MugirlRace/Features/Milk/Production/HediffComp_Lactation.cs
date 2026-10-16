namespace Mugirl.Features.Milk;

// 哺乳期 HediffComp：按产后天数与饥饿状态调整雪牛娘的产奶倍率。
public sealed class HediffComp_Lactation : HediffComp
{
    // 存档类型保持 float（"mugirlLactationLastBirthTick"）；计算时转成 double，避免大 tick 值的 float 舍入。
    private float mugirlLactationLastBirthTick = -1f;

    public HediffCompProperties_Lactation? Props => props as HediffCompProperties_Lactation;

    public float ProductionMultiplier
    {
        get
        {
            HediffCompProperties_Lactation? lactationProps = Props;
            if (lactationProps == null)
            {
                return 1f;
            }

            float multiplier = UnityMathf.Max(0f, lactationProps.baseProductionMultiplier);
            if (mugirlLactationLastBirthTick > 0f && TickUtility.TryGetCurrentGameTick(out int currentTick))
            {
                double daysSinceBirth = Math.Max(0d, (currentTick - (double)mugirlLactationLastBirthTick) / GenDate.TicksPerDay);
                if (daysSinceBirth < lactationProps.postpartumBoostDays)
                {
                    multiplier *= UnityMathf.Max(0f, lactationProps.postpartumMultiplier);
                }
            }

            HungerCategory? hungerCategory = Pawn?.needs?.food?.CurCategory;
            if (hungerCategory == HungerCategory.UrgentlyHungry || hungerCategory == HungerCategory.Starving)
            {
                multiplier *= UnityMathf.Max(0f, lactationProps.malnourishedPenalty);
            }

            return multiplier;
        }
    }

    public void NotifyBirth()
    {
        if (TickUtility.TryGetCurrentGameTick(out int currentTick))
        {
            mugirlLactationLastBirthTick = currentTick;
        }
    }

    public override void CompExposeData()
    {
        base.CompExposeData();
        Scribe_Values.Look(ref mugirlLactationLastBirthTick, "mugirlLactationLastBirthTick", -1f);
    }
}
