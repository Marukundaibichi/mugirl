using Verse;

namespace MooGirl
{
    // Hediff 消失后在同一部位追加另一个 Hediff。
    public class CompProperties_DisappearsAndAddHediffs : HediffCompProperties_Disappears
    {
        public HediffDef hediffToAddOnDisappear;

        public CompProperties_DisappearsAndAddHediffs()
        {
            compClass = typeof(HediffComp_DisappearsAndAddHediffs);
        }
    }

    public class HediffComp_DisappearsAndAddHediffs : HediffComp_Disappears
    {
        public new CompProperties_DisappearsAndAddHediffs Props => props as CompProperties_DisappearsAndAddHediffs;

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();

            CompProperties_DisappearsAndAddHediffs compProps = Props;
            if (compProps?.hediffToAddOnDisappear != null && Pawn?.health != null && parent?.Part != null)
            {
                Pawn.health.AddHediff(compProps.hediffToAddOnDisappear, parent.Part);
            }
        }
    }
}
