using Verse;

namespace MooGirl
{
    // 定义洗脑效果组件，继承自HediffComp，实现具体的洗脑逻辑
    public class HediffComp_BrainWashingStar : HediffComp
    {
        private BrainwashPerformancePlayer performancePlayer = new BrainwashPerformancePlayer();

        // 快捷属性，获取配置参数
        public CompProperties_PerformanceEffect Props => props as CompProperties_PerformanceEffect;

        // 组件初始化后调用
        public override void CompPostMake()
        {
            base.CompPostMake();
            performancePlayer.Start(Props);
        }

        // 每帧调用，处理效果逻辑
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

        // 数据保存/加载
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
