namespace Mugirl.Features.Incidents;

public class FloatMenuOptionProvider_TalkToCourier : FloatMenuOptionProvider_TalkToEventPawn
{
    protected override JobDef TalkJob => IncidentsDefOf.Mugirl_TalkCourier;

    protected override bool CanTalkTo(Pawn target) => CourierConversationUtility.CanTalkToCourier(target);

    protected override bool CanNegotiate(Pawn actor, Pawn target) => CourierConversationUtility.CanNegotiateWithCourier(actor, target);

    protected override TaggedString OptionLabel(Pawn target) => "Mugirl.CourierTalkOption".Translate(target.Named("COURIER"));

    protected override TaggedString DisabledLabel(string optionLabel)
        => "Mugirl.CourierTalkOptionDisabled".Translate(optionLabel, "Mugirl.CourierTalkCannotReach".Translate());
}
