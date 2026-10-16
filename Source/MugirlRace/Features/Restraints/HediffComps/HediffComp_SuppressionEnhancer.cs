namespace Mugirl.Features.Restraints;

// 殖民地奴隶佩戴已破解的洗脑头盔时，每个周期补充一次压制度。
public class HediffComp_SuppressionEnhancer : HediffComp
{
    private int age;

    public HediffCompProperties_SuppressionEnhancer? Props => props as HediffCompProperties_SuppressionEnhancer;

    public override void CompPostTick(ref float severityAdjustment)
    {
        base.CompPostTick(ref severityAdjustment);

        HediffCompProperties_SuppressionEnhancer? compProps = Props;
        if (compProps == null)
        {
            return;
        }

        age++;
        if (age < Math.Max(1, compProps.triggerTicks))
        {
            return;
        }

        age = 0;

        Pawn? pawn = Pawn;
        if (pawn?.IsSlaveOfColony == true && pawn.IsWearingCrackedBrainwashApparel())
        {
            ApplySuppression(pawn, compProps);
        }
    }

    public override void CompExposeData()
    {
        base.CompExposeData();
        Scribe_Values.Look(ref age, "mugirlSuppressionAge", 0);
    }

    private static void ApplySuppression(Pawn pawn, HediffCompProperties_SuppressionEnhancer compProps)
    {
        Need_Suppression? suppression = pawn.needs?.TryGetNeed<Need_Suppression>();
        if (suppression == null)
        {
            return;
        }

        float amount = UnityMathf.Max(0f, compProps.enhancedMode ? compProps.enhancedAmount : compProps.regularAmount);
        if (amount <= 0f)
        {
            return;
        }

        suppression.CurLevelPercentage = UnityMathf.Clamp01(suppression.CurLevelPercentage + amount);

        if (pawn.Map != null)
        {
            // Unity 的 Color.yellow 精确值（UnityStubs 的近似值略有不同），与 1.6 显示一致。
            MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "Mugirl.SuppressionIncrease".Translate(amount.ToStringPercent()), UnityColor.Yellow, 6f);
        }
    }
}
