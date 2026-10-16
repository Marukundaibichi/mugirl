namespace Mugirl.Features.Incidents;

// XML driverClass，类名写进存档，不能改名。
public class JobDriver_TalkToFusionInvestor : JobDriver_TalkToEventPawn
{
    protected override bool CanNegotiate(Pawn negotiator, Pawn? target) => FusionInvestmentUtility.CanNegotiateWithInvestor(negotiator, target);

    protected override void ShowDialog(Pawn? target, Pawn negotiator) => FusionInvestmentUtility.ShowDialog(target, negotiator);
}
