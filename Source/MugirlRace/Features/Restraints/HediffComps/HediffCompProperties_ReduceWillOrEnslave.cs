using Mugirl.Features.Restraints;

namespace Mugirl.Features.Restraints;

public class HediffCompProperties_ReduceWillOrEnslave : HediffCompProperties
{
    // 达到触发时间后可一次性削减意志或直接奴役囚犯。
    public int triggerTicks = 600;
    public bool reduceWill;
    public bool makeSlave;
    public float willReductionAmount = 10f;

    public HediffCompProperties_ReduceWillOrEnslave()
    {
        compClass = typeof(HediffComp_ReduceWillOrEnslave);
    }
}
