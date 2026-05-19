using Verse;

namespace MooGirl
{
    // 新的 HediffComp 属性类，继承消失组件的属性类（可复用）
    public class CompProperties_DisappearsAndAddHeiffs : HediffCompProperties_Disappears
    {
        public HediffDef hediffToAddOnDisappear; // 消失时添加的新Hediff

        public CompProperties_DisappearsAndAddHeiffs()
        {
            compClass = typeof(HediffComp__DisappearsAndAddHeiffs);
        }
    }

    // 继承自 HediffComp_Disappears，增加消失时添加新 Hediff 的功能
    public class HediffComp__DisappearsAndAddHeiffs : HediffComp_Disappears
    {
        public new CompProperties_DisappearsAndAddHeiffs Props => (CompProperties_DisappearsAndAddHeiffs)props;

        // 重写消失后调用函数
        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();

            // 消失时如果配置了新Hediff，则添加它
            if (Props.hediffToAddOnDisappear != null && Pawn != null && parent.Part != null)
            {
                // 添加指定Hediff，添加在与当前Hediff相同的部位上
                Pawn.health.AddHediff(Props.hediffToAddOnDisappear, parent.Part);
            }
        }
    }
}
