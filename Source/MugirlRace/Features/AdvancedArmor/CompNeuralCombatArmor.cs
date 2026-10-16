namespace Mugirl.Features.AdvancedArmor;

// 穿上时附加配置的 Hediff，脱下时移除。
public sealed class CompNeuralCombatArmor : ThingComp
{
    private CompProperties_NeuralCombatArmor Props => (CompProperties_NeuralCombatArmor)props;

    public override void Notify_Equipped(Pawn pawn)
    {
        base.Notify_Equipped(pawn);
        HediffDef? hediffDef = Props.hediff;
        if (pawn?.health is not null && hediffDef is not null && !pawn.health.hediffSet.HasHediff(hediffDef))
        {
            pawn.health.AddHediff(hediffDef);
        }
    }

    public override void Notify_Unequipped(Pawn pawn)
    {
        base.Notify_Unequipped(pawn);
        HediffDef? hediffDef = Props.hediff;
        if (hediffDef is not null && pawn?.health?.hediffSet?.GetFirstHediffOfDef(hediffDef) is { } hediff)
        {
            pawn.health.RemoveHediff(hediff);
        }
    }
}
