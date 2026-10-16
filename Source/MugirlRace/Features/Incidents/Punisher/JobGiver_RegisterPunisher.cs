using Verse.AI;

namespace Mugirl.Features.Incidents;

// XML ThinkTree 节点：惩罚者每次思考时确认已在 MapComponent 中登记（例如开发者生成的惩罚者），本身不发工作。
public class JobGiver_RegisterPunisher : ThinkNode_JobGiver
{
    protected override Job? TryGiveJob(Pawn pawn)
    {
        pawn?.Map?.GetComponent<MapComponent_Punisher>()?.EnsureRegistered(pawn);
        return null;
    }
}
