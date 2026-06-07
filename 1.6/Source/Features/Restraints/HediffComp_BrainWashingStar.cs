using Verse;

namespace MooGirl
{
    // 洗脑表演 HediffComp，持有可存档的播放状态。
    public class HediffComp_BrainWashingStar : HediffComp
    {
        private BrainwashPerformancePlayer performancePlayer = new BrainwashPerformancePlayer();

        public CompProperties_PerformanceEffect Props => props as CompProperties_PerformanceEffect;

        public override void CompPostMake()
        {
            base.CompPostMake();
            performancePlayer.Start(Props);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            if (performancePlayer == null)
            {
                performancePlayer = new BrainwashPerformancePlayer();
                performancePlayer.Start(Props);
            }

            performancePlayer.Tick(Pawn, Props);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Deep.Look(ref performancePlayer, "performancePlayer");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (performancePlayer == null)
                    performancePlayer = new BrainwashPerformancePlayer();
            }
        }
    }
}
