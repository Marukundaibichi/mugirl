namespace Mugirl.Features.Milk;

// 食用时添加食物 Hediff；已存在时刷新持续时间，并按需提高严重度。
public class IngestionOutcomeDoer_AddOrRefreshHediff : IngestionOutcomeDoer
{
    public HediffDef? hediffDef;

    public float severity = -1f;

    protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
    {
        FoodEffectUtility.AddOrRefreshHediff(pawn, hediffDef, severity);
    }
}
