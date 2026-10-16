using Mugirl.Features.Incidents;

namespace Mugirl.SaveUpgrade;

internal sealed class PunisherInitialization : IPunisherInitialization
{
    internal int firstUnscheduledIndex = -1;

    public PunisherInitialization()
    {
    }

    public void Complete(List<TrackedPunisher> members)
    {
        int currentTick = TickUtility.CurrentGameTickOrFallback(0);
        if (firstUnscheduledIndex >= 0)
        {
            for (int i = firstUnscheduledIndex; i < members.Count; i++)
            {
                members[i].spawnTick = currentTick;
            }
        }
    }

    public void ExposeData() => Scribe_Values.Look(ref firstUnscheduledIndex, "firstUnscheduledIndex", -1);
}
