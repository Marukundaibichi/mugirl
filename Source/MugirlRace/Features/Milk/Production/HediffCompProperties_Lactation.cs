namespace Mugirl.Features.Milk;

// 哺乳期 HediffComp 的 XML 配置：基础倍率、产后加成窗口与饥饿惩罚。
public class HediffCompProperties_Lactation : HediffCompProperties
{
    public float baseProductionMultiplier = 1.0f;

    public float postpartumBoostDays = 7f;

    public float postpartumMultiplier = 2.5f;

    public float malnourishedPenalty = 0.3f;

    public HediffCompProperties_Lactation()
    {
        compClass = typeof(HediffComp_Lactation);
    }
}
