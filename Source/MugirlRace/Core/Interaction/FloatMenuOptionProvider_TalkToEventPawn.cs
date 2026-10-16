using Verse.AI;

namespace Mugirl.Core;

// 右键“与某个事件 Pawn 交谈”的共用菜单：运货员与牛聚变投资人只在标签、条件和 JobDef 上不同。
// 原版按反射为每个非抽象子类建一个实例，本基类是抽象类，不会被单独实例化。
public abstract class FloatMenuOptionProvider_TalkToEventPawn : FloatMenuOptionProvider
{
    protected override bool Drafted => true;

    protected override bool Undrafted => true;

    protected override bool Multiselect => false;

    protected override bool RequiresManipulation => true;

    protected abstract JobDef TalkJob { get; }

    protected abstract bool CanTalkTo(Pawn target);

    protected abstract bool CanNegotiate(Pawn actor, Pawn target);

    protected abstract TaggedString OptionLabel(Pawn target);

    /// <summary>不可达时的标签：原标签与原因组合。</summary>
    protected abstract TaggedString DisabledLabel(string optionLabel);

    public override bool TargetPawnValid(Pawn target, FloatMenuContext context)
    {
        return base.TargetPawnValid(target, context) && CanTalkTo(target);
    }

    public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
    {
        Pawn? actor = context.FirstSelectedPawn;
        if (actor == null || !PlayerFactionUtility.IsPlayerFaction(actor.Faction) || actor.RaceProps?.Humanlike != true)
        {
            yield break;
        }

        FloatMenuOption option = FloatMenuUtility.DecoratePrioritizedTask(
            new FloatMenuOption(OptionLabel(clickedPawn), () =>
            {
                TryStartTalkJob(actor, clickedPawn);
            }, MenuOptionPriority.High),
            actor,
            clickedPawn);

        if (!actor.CanReserveAndReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
        {
            option.Disabled = true;
            option.Label = DisabledLabel(option.Label);
        }

        yield return option;
    }

    private void TryStartTalkJob(Pawn actor, Pawn target)
    {
        if (!CanNegotiate(actor, target)
            || !actor.CanReserveAndReach(target, PathEndMode.Touch, Danger.Deadly))
        {
            return;
        }

        Job job = JobMaker.MakeJob(TalkJob, target);
        actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
    }
}
