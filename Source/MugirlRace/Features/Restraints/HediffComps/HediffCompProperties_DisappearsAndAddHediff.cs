namespace Mugirl.Features.Restraints;

// Hediff 计时结束自然消失后，在同一部位追加另一个 Hediff（洗脑阶段推进）。
public class HediffCompProperties_DisappearsAndAddHediff : HediffCompProperties_Disappears
{
    public HediffDef? hediffToAddOnDisappear;

    public HediffCompProperties_DisappearsAndAddHediff()
    {
        compClass = typeof(HediffComp_DisappearsAndAddHediff);
    }
}
