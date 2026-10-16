using Verse.AI;
using Verse.AI.Group;

namespace Mugirl.Features.Corporate;

// 支援小队：索敌一天后撤离，没有因伤亡、受损或士气触发的撤退分支（类名随 Lord 写入存档）。
public sealed class LordJob_CorporateSupport : LordJob
{
    private IntVec3 fallback;
    internal ICorporateSupportContinuation? continuation;

    public LordJob_CorporateSupport()
    {
    }

    public LordJob_CorporateSupport(IntVec3 fallback)
    {
        this.fallback = fallback;
    }

    // 队伍的持久完成动作可要求装载其对应的状态图，普通支援队没有额外逃跑分支。
    public override bool AddFleeToil => continuation?.IncludeFleeToil == true;

    public override StateGraph CreateGraph()
    {
        var graph = new StateGraph();
        // 搜敌 toil 使用本模组 duty，空闲时队员会处理自己或附近友方小人的伤口。
        var fight = new LordToil_CorporateSupportHunt(fallback);
        var exit = new LordToil_ExitMap(LocomotionUrgency.Jog, canDig: true);
        graph.AddToil(fight);
        graph.AddToil(exit);
        var leave = new Transition(fight, exit);
        leave.AddTrigger(new Trigger_TicksPassed(CorporateNetwork.DayTicks));
        leave.AddPostAction(new TransitionAction_EndAllJobs());
        graph.AddTransition(leave);
        return graph;
    }

    public override void LordJobTick()
    {
        ICorporateSupportContinuation? pending = continuation;
        continuation = null;
        pending?.Complete(lord);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref fallback, "fallback");
        Scribe_Deep.Look(ref continuation, "continuation");
    }
}
