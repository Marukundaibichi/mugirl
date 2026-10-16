namespace Mugirl.Features.Incidents;

public class FloatMenuOptionProvider_TalkToFusionInvestor : FloatMenuOptionProvider_TalkToEventPawn
{
    protected override JobDef TalkJob => IncidentsDefOf.Mugirl_TalkFusionInvestor;

    protected override bool CanTalkTo(Pawn target) => FusionInvestmentUtility.CanTalkToInvestor(target);

    protected override bool CanNegotiate(Pawn actor, Pawn target) => FusionInvestmentUtility.CanNegotiateWithInvestor(actor, target);

    protected override TaggedString OptionLabel(Pawn target) => "Mugirl.FusionInvestment.TalkOption".Translate();

    protected override TaggedString DisabledLabel(string optionLabel)
        => "Mugirl.FusionInvestment.TalkOptionDisabled".Translate(optionLabel, "Mugirl.FusionInvestment.TalkCannotReach".Translate());
}
