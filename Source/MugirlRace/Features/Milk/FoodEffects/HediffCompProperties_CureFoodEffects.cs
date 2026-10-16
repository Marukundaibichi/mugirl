namespace Mugirl.Features.Milk;

// 持续清除指定 Hediff 的食物效果配置。removeHediffs 在 XML 中仍写 defName，由原版交叉引用解析为 HediffDef。
public class HediffCompProperties_CureFoodEffects : HediffCompProperties
{
    public List<HediffDef> removeHediffs = [];

    public HediffCompProperties_CureFoodEffects()
    {
        compClass = typeof(HediffComp_CureFoodEffects);
    }
}
