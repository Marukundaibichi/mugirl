using RimWorld.Planet;

namespace Mugirl.Features.Corporate;

public class QuestPart_CorporateMission : QuestPart
{
    public int missionId;

    public override string DescriptionPart => CorporateNetwork.Current?.FindMission(missionId)?.Description ?? "";

    public override IEnumerable<GlobalTargetInfo> QuestLookTargets
    {
        get
        {
            CorporateMission? m = CorporateNetwork.Current?.FindMission(missionId);
            if (m?.site != null && m.site.Spawned)
            {
                yield return m.site;
            }
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref missionId, "missionId");
    }
}
