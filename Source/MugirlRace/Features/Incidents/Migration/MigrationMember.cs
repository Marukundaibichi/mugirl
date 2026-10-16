namespace Mugirl.Features.Incidents;

internal sealed class MigrationMember : IExposable
{
    internal Pawn? pawn;
    internal int grazeAtTick;

    public MigrationMember()
    {
    }

    internal MigrationMember(Pawn pawn, int grazeAtTick)
    {
        this.pawn = pawn;
        this.grazeAtTick = grazeAtTick;
    }

    public void ExposeData()
    {
        Scribe_References.Look(ref pawn, "pawn");
        Scribe_Values.Look(ref grazeAtTick, "grazeAtTick", 0, forceSave: true);
    }
}
