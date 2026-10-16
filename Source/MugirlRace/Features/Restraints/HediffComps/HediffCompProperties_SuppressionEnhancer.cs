using Mugirl.Features.Restraints;

namespace Mugirl.Features.Restraints;

public class HediffCompProperties_SuppressionEnhancer : HediffCompProperties
{
    // 每隔一段时间补充奴隶压制度，可切换为加强模式。
    public int triggerTicks = 600;
    public bool enhancedMode;
    public float regularAmount = 0.10f;
    public float enhancedAmount = 0.30f;

    public HediffCompProperties_SuppressionEnhancer()
    {
        compClass = typeof(HediffComp_SuppressionEnhancer);
    }
}
