namespace Mugirl.Features.Milk;

// 婴儿吸奶的纯计算，逐项对应原版 1.7 ChildcareUtility.SuckleFromLactatingPawn。
internal static class MilkSuckleMath
{
    // 原版：每 tick 至多补满 1/5000 的食物上限，且不超过婴儿当前想要的营养。
    internal const float FullFeedTicksPerMaxLevel = 5000f;

    internal static float DesiredNutrition(float maxLevel, float nutritionWanted, int delta)
    {
        return UnityMathf.Min(maxLevel / FullFeedTicksPerMaxLevel * delta, nutritionWanted);
    }

    // 本次是否继续吸奶：已喝够婴儿想要的量就停止，奶不够本次所需时也停止。
    // 镜像原版：1.7 原版调用 UnityStubs 的 Mathf.Approximately（<= 比较），因此用 ApproximatelyInclusive。
    internal static bool ShouldContinue(float consumedNutrition, float nutritionWanted, float desiredNutrition)
    {
        if (UnityMathf.ApproximatelyInclusive(consumedNutrition, nutritionWanted))
        {
            return false;
        }

        return consumedNutrition >= desiredNutrition;
    }

    // 奶量（0-1 饱满度）可提供的营养；每 1.0 饱满度对应 nutritionPerFullness 营养。
    internal static float AvailableNutrition(float fullness, float nutritionPerFullness)
    {
        return UnityMathf.Max(0f, fullness) * nutritionPerFullness;
    }
}
