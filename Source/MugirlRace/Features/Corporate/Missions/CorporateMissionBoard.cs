namespace Mugirl.Features.Corporate;

// 委托及支线进度；委托编号独立于交易编号，保留所有合同字段。
internal sealed class CorporateMissionBoard : IExposable
{
    internal List<CorporateMission> missions = new List<CorporateMission>();
    internal int nextId = 1;
    internal int nextCheck;
    internal bool fusionQualified;
    internal bool fusionInvited;
    internal int fusionDueTick = -1;
    internal Pawn? retainedInvestor;
    internal string? retainedInvestorName;

    internal int ActiveCount
    {
        get
        {
            int count = 0;
            foreach (CorporateMission mission in missions)
            {
                if (!mission.IsSide && (mission.state == CorporateMissionState.Active || mission.state == CorporateMissionState.Ready))
                {
                    count++;
                }
            }

            return count;
        }
    }

    internal CorporateMission Create(CorporateMissionKind kind, int now) =>
        new() { id = nextId++, kind = kind, offeredTick = now, acceptByTick = now + CorporateNetwork.WeekTicks };

    internal CorporateMission? Find(int id) => missions.Find(mission => mission.id == id);

    public void ExposeData()
    {
        Scribe_Collections.Look(ref missions, "missions", LookMode.Deep);
        Scribe_Values.Look(ref nextId, "nextId", 1);
        Scribe_Values.Look(ref fusionQualified, "fusionQualified");
        Scribe_Values.Look(ref fusionInvited, "fusionInvited");
        Scribe_Values.Look(ref fusionDueTick, "fusionDueTick", -1);
        Scribe_References.Look(ref retainedInvestor, "retainedInvestor");
        Scribe_Values.Look(ref retainedInvestorName, "retainedInvestorName");
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            missions ??= new List<CorporateMission>();
            missions.RemoveAll(m => m == null);
        }
    }
}
