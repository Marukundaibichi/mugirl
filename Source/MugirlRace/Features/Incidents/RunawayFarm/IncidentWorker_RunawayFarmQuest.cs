namespace Mugirl.Features.Incidents;

// IncidentDef Mugirl_RunawayFarmQuest：原版发任务流程，只加设置开关。
public class IncidentWorker_RunawayFarmQuest : IncidentWorker_GiveQuest_Map
{
    protected override bool CanFireNowSub(IncidentParms parms)
    {
        return MugirlMod.Settings?.enableRunawayFarmQuest == true
            && base.CanFireNowSub(parms);
    }
}
