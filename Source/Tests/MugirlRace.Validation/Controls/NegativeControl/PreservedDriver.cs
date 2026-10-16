using Verse.AI;

namespace Mugirl.Control.Upgrade;

public sealed class PreservedDriver : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
    protected override IEnumerable<Toil> MakeNewToils() => [];
}
