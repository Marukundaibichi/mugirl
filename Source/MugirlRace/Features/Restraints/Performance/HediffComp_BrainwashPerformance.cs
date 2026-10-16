namespace Mugirl.Features.Restraints;

// 洗脑表演 HediffComp，持有可存档的播放状态。类名是 HediffCompProperties_BrainwashPerformance 的 compClass 与 DefInjected 句柄。
public class HediffComp_BrainwashPerformance : HediffComp
{
    private BrainwashPerformancePlayer mugirlBrainwashPerformance = new BrainwashPerformancePlayer();

    public HediffCompProperties_BrainwashPerformance? Props => props as HediffCompProperties_BrainwashPerformance;

    public override void CompPostMake()
    {
        base.CompPostMake();
        mugirlBrainwashPerformance.Start(Props);
    }

    public override void CompPostTick(ref float severityAdjustment)
    {
        base.CompPostTick(ref severityAdjustment);
        if (mugirlBrainwashPerformance == null)
        {
            mugirlBrainwashPerformance = new BrainwashPerformancePlayer();
            mugirlBrainwashPerformance.Start(Props);
        }

        mugirlBrainwashPerformance.Tick(Pawn, Props);
    }

    public override void CompExposeData()
    {
        base.CompExposeData();
        Scribe_Deep.Look(ref mugirlBrainwashPerformance, "mugirlBrainwashPerformance");

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            mugirlBrainwashPerformance ??= new BrainwashPerformancePlayer();
        }
    }
}
