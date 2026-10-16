namespace Mugirl.Features.Corporate;

public class SitePartWorker_CorporateMission : SitePartWorker
{
    public override void PostMapGenerate(Map map)
    {
        base.PostMapGenerate(map);
        CorporateNetwork.Current?.SpawnMissionSite(map);
    }
}
