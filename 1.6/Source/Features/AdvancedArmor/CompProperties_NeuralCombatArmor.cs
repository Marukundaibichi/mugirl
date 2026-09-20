using RimWorld;
using Verse;

namespace Mugirl.Features.AdvancedArmor
{
    public sealed class CompProperties_NeuralCombatArmor : CompProperties
    {
        public HediffDef hediff;

        public CompProperties_NeuralCombatArmor()
        {
            compClass = typeof(CompNeuralCombatArmor);
        }
    }

    public sealed class CompNeuralCombatArmor : ThingComp
    {
        private CompProperties_NeuralCombatArmor Props => (CompProperties_NeuralCombatArmor)props;

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            HediffDef hediffDef = Props.hediff;
            if (pawn?.health != null && hediffDef != null && !pawn.health.hediffSet.HasHediff(hediffDef))
            {
                pawn.health.AddHediff(hediffDef);
            }
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            Hediff hediff = pawn?.health?.hediffSet?.GetFirstHediffOfDef(Props.hediff);
            if (hediff != null)
            {
                pawn.health.RemoveHediff(hediff);
            }
        }
    }
}
