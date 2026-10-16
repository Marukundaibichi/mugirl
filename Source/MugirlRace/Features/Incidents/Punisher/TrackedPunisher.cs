namespace Mugirl.Features.Incidents;

internal sealed class TrackedPunisher : IExposable
{
    internal Pawn? pawn;
    internal int spawnTick;
    internal bool leaving;

    public TrackedPunisher()
    {
    }

    internal TrackedPunisher(Pawn pawn, int spawnTick)
    {
        this.pawn = pawn;
        this.spawnTick = spawnTick;
    }

    public void ExposeData()
    {
        Scribe_References.Look(ref pawn, "pawn");
        Scribe_Values.Look(ref spawnTick, "spawnTick", 0, forceSave: true);
        Scribe_Values.Look(ref leaving, "leaving", false);
    }
}
