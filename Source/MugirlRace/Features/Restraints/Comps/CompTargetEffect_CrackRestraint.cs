namespace Mugirl.Features.Restraints;

public class CompTargetEffect_CrackRestraint : CompTargetEffect
{
    public override void DoEffectOn(Pawn user, Thing target)
    {
        if (target is AdvancedRestraintApparel apparel && !apparel.IsCracked())
        {
            string key = apparel is BrainwashHelmetApparel ? "Mugirl.CrackBondageGear_CrackBrainwash" : "Mugirl.CrackBondageGear_CrackAdvanced";
            Messages.Message(key.Translate(apparel.Label), apparel, MessageTypeDefOf.PositiveEvent, historical: false);
            apparel.Crack();
            return;
        }

        Messages.Message("Mugirl.CrackBondageGear_TargetNotValid".Translate(), MessageTarget(user, target), MessageTypeDefOf.RejectInput, historical: false);
    }

    private static Thing MessageTarget(Pawn? user, Thing fallback)
    {
        return user ?? fallback;
    }
}
