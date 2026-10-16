namespace Mugirl.Features.Milk;

// 食用时立即移除指定 Hediff。removeHediffs 在 XML 中仍写 defName，由原版交叉引用解析为 HediffDef。
public class IngestionOutcomeDoer_RemoveHediffs : IngestionOutcomeDoer
{
    public List<HediffDef> removeHediffs = [];

    protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
    {
        FoodEffectUtility.RemoveHediffs(pawn, removeHediffs);
    }
}
