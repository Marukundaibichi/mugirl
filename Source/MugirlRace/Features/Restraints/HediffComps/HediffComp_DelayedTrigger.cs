namespace Mugirl.Features.Restraints;

// 洗脑类“延迟一次性触发”HediffComp 的公共基类（AddMentalState、AddThought、AddTrait、ReduceWillorEnslave）。
// Hediff 存在满触发时长后每 tick 尝试一次，成功后不再执行。计时与 1.6 相同在 CompPostTick 中逐 tick 累计。
// 每个子类使用独有的存档标签；计时和完成状态独立保存。
public abstract class HediffComp_DelayedTrigger : HediffComp
{
    private int age;
    private bool triggered;

    // 配置的触发时长（tick，小于 1 时按 1）；CompProperties 缺失时为 null，此时只计时不触发（同 1.6）。
    protected abstract int? ConfiguredTriggerTicks { get; }

    protected abstract string AgeScribeLabel { get; }

    protected abstract string TriggeredScribeLabel { get; }

    public override void CompPostTick(ref float severityAdjustment)
    {
        base.CompPostTick(ref severityAdjustment);
        if (triggered)
        {
            return;
        }

        age++;

        int? triggerTicks = ConfiguredTriggerTicks;
        Pawn? pawn = Pawn;
        if (triggerTicks == null || pawn == null || age < Math.Max(1, triggerTicks.Value))
        {
            return;
        }

        if (TryTrigger(pawn))
        {
            triggered = true;
        }
    }

    public override void CompExposeData()
    {
        base.CompExposeData();
        Scribe_Values.Look(ref age, AgeScribeLabel, 0);
        Scribe_Values.Look(ref triggered, TriggeredScribeLabel, false);
    }

    // 到时后每 tick 调用一次；返回 true 表示已处理，之后不再调用。
    protected abstract bool TryTrigger(Pawn pawn);
}
