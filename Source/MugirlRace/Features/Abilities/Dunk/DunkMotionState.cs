namespace Mugirl.Features.Abilities;

// 飞行与演出使用同一份可持久状态；阶段和剩余进度不由其他字段推算。
internal sealed class DunkMotionState : IExposable
{
    public DunkMotionState()
    {
    }

    internal DunkPhase phase;
    internal int phaseTicks;
    internal int runningTicks;
    internal UVector3 launchHeadPosition;
    internal UVector3 launchCarrierPosition;
    internal UVector3 impactStartHeadPosition;
    internal UVector3 impactStartCarrierPosition;
    internal bool followsCarrier;
    internal bool impactAnimating;
    internal bool impactTracksCarrier;
    internal float launchSpinDegrees;
    internal float impactStartSpinDegrees;

    public void ExposeData()
    {
        Scribe_Values.Look(ref phase, "phase", DunkPhase.Dribbling);
        Scribe_Values.Look(ref phaseTicks, "phaseTicks", 0);
        Scribe_Values.Look(ref runningTicks, "runningTicks", 0);
        Scribe_Values.Look(ref launchHeadPosition, "launchHeadPosition");
        Scribe_Values.Look(ref launchCarrierPosition, "launchCarrierPosition");
        Scribe_Values.Look(ref impactStartHeadPosition, "impactStartHeadPosition");
        Scribe_Values.Look(ref impactStartCarrierPosition, "impactStartCarrierPosition");
        Scribe_Values.Look(ref followsCarrier, "followsCarrier", false);
        Scribe_Values.Look(ref impactAnimating, "impactAnimating", false);
        Scribe_Values.Look(ref impactTracksCarrier, "impactTracksCarrier", false);
        Scribe_Values.Look(ref launchSpinDegrees, "launchSpinDegrees", 0f);
        Scribe_Values.Look(ref impactStartSpinDegrees, "impactStartSpinDegrees", 0f);
    }
}
