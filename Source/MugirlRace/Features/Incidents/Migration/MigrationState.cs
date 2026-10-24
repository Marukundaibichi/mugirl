namespace Mugirl.Features.Incidents;

// 迁徙批次的事实与剩余计时。成员表允许空 Pawn 引用，直到正常 tick 判定结局。
internal sealed class MigrationState : IExposable
{
    internal List<MigrationMember> members = [];
    internal List<int> playerKillIds = new List<int>();
    internal IntVec3 exit = IntVec3.Invalid;
    internal int departureRemaining;
    internal int killWindowRemaining;
    internal int initialCount;
    internal int playerKillCount;
    internal bool outcomeFailed;
    internal bool departing;

    internal bool Contains(Pawn pawn) => IndexOf(pawn) >= 0;

    internal int IndexOf(Pawn pawn)
    {
        for (int i = 0; i < members.Count; i++)
        {
            if (EqualityComparer<Pawn?>.Default.Equals(members[i].pawn, pawn))
            {
                return i;
            }
        }

        return -1;
    }

    internal void NormalizeCollections()
    {
        members ??= [];
        playerKillIds ??= [];
    }

    public void ExposeData()
    {
        Scribe_Collections.Look(ref members, "members", LookMode.Deep);
        Scribe_Collections.Look(ref playerKillIds, "playerKillIds", LookMode.Value);
        Scribe_Values.Look(ref exit, "exit", IntVec3.Invalid);
        Scribe_Values.Look(ref departureRemaining, "departureRemaining", 0);
        Scribe_Values.Look(ref killWindowRemaining, "killWindowRemaining", 0);
        Scribe_Values.Look(ref initialCount, "initialCount", 0);
        Scribe_Values.Look(ref playerKillCount, "playerKillCount", 0);
        Scribe_Values.Look(ref outcomeFailed, "outcomeFailed", false);
        Scribe_Values.Look(ref departing, "departing", false);
    }
}
