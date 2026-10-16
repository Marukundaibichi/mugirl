namespace Mugirl.Features.Incidents;

internal interface IPunisherInitialization : IExposable
{
    void Complete(List<TrackedPunisher> members);
}
