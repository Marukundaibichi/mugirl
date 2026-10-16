using Mugirl.Features.Corporate;

namespace Mugirl.Features.Incidents;

// IncidentDef Mugirl_CourierRaid（基础几率 0，只供开发者或手动触发）：直接发布运货员任务。
public class IncidentWorker_CourierContact : IncidentWorker
{
    private const float MaxQuestPoints = 200f;

    protected override bool CanFireNowSub(IncidentParms parms)
    {
        if (!base.CanFireNowSub(parms))
        {
            return false;
        }

        if (parms.target is not Map)
        {
            return false;
        }

        return GameUtility.TryGetFirstFactionOfDef(CorporateDefOf.Mugirl_GiantCorporation, out _);
    }

    protected override bool TryExecuteWorker(IncidentParms parms)
    {
        if (parms.target is not Map map)
        {
            return false;
        }

        if (!GameUtility.TryGetFirstFactionOfDef(CorporateDefOf.Mugirl_GiantCorporation, out _))
        {
            return false;
        }

        // parms.points 为 NaN 时 NaN > 0 不成立，同样取上限。
        float points = parms.points > 0f ? UnityMathf.Min(parms.points, MaxQuestPoints) : MaxQuestPoints;
        if (QuestLauncher.TryLaunch<QuestPart_SpawnCourier>(IncidentsDefOf.Mugirl_CourierContact, map, points) == null)
        {
            return false;
        }

        // 记入剧情状态，自动调度不会再发第二个一次性运货员。
        if (GameUtility.TryGetGameComponent<GameComponent_Storyline>(out GameComponent_Storyline? state))
        {
            state.MarkCourierRaidStarted();
            state.WakeStoryService();
        }

        return true;
    }
}
