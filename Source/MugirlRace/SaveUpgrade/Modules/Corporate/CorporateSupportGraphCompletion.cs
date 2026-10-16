using Mugirl.Features.Corporate;
using Verse.AI;
using Verse.AI.Group;

namespace Mugirl.SaveUpgrade;

// supportGraphVersion=0 的完成动作：先装载原有图和所有索引，严格保留原首次 LordJobTick 的清理时点。
// schema 1 在首次 tick 前保存时，Class 仍持久化，下一进程可重新构建同一张图。
internal sealed class CorporateSupportGraphCompletion : ICorporateSupportContinuation
{
    public CorporateSupportGraphCompletion()
    {
    }

    public bool IncludeFleeToil => Scribe.mode == LoadSaveMode.PostLoadInit;

    public void Complete(Lord lord)
    {
        bool wasFleeing = lord.CurLordToil is LordToil_PanicFlee;
        if (wasFleeing)
        {
            lord.GotoToil(lord.Graph.lordToils[0]);
        }

        foreach (Pawn pawn in lord.ownedPawns)
        {
            if (wasFleeing || pawn.CurJobDef == JobDefOf.TendPatient)
            {
                pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            }
        }

        lord.Graph.transitions.RemoveAll(t => t.target is LordToil_PanicFlee);
        lord.Graph.lordToils.RemoveAll(t => t is LordToil_PanicFlee);
    }

    public void ExposeData()
    {
    }
}
