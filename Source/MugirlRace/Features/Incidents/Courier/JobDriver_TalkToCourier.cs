namespace Mugirl.Features.Incidents;

// XML driverClass，类名写进存档，不能改名。
public class JobDriver_TalkToCourier : JobDriver_TalkToEventPawn
{
    protected override bool CanNegotiate(Pawn negotiator, Pawn? target) => CourierConversationUtility.CanNegotiateWithCourier(negotiator, target);

    protected override void ShowDialog(Pawn? target, Pawn negotiator) => CourierConversationUtility.ShowDialog(target, negotiator);
}
