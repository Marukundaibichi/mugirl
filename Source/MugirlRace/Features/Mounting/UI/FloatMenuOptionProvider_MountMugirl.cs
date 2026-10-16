using Verse.AI;

namespace Mugirl.Features.Mounting;

// 右键雪牛娘时提供骑乘/下骑选项（原版按反射发现 FloatMenuOptionProvider 子类）。
public class FloatMenuOptionProvider_MountMugirl : FloatMenuOptionProvider
{
    protected override bool Drafted => true;
    protected override bool Undrafted => true;
    protected override bool Multiselect => false;
    protected override bool CanSelfTarget => true;

    public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context)
    {
        return base.SelectedPawnValid(pawn, context) && pawn is not null && pawn.Spawned;
    }

    public override bool TargetPawnValid(Pawn target, FloatMenuContext context)
    {
        return base.TargetPawnValid(target, context)
            && target is not null
            && target.Spawned
            && !target.Dead
            && MountUtility.GetMountComp(target) is not null
            && MountedPawnUtility.IsMugirl(target);
    }

    public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
    {
        Pawn? actor = context.FirstSelectedPawn;
        Pawn? moo = clickedPawn;
        if (actor is null || moo is null || MountUtility.GetMountComp(moo) is not { } comp)
        {
            yield break;
        }

        if (actor == moo)
        {
            if (comp.HasMountedPawn)
            {
                yield return new FloatMenuOption("Mugirl.Mount.DismountRider".Translate(), () => comp.TryDismount(), MenuOptionPriority.High, null, moo);
            }

            yield break;
        }

        if (comp.HasMountedPawn)
        {
            string dismountLabel = "Mugirl.Mount.DismountRider".Translate();
            if (TryGetBlockedOption(actor, moo, dismountLabel) is { } blockedDismount)
            {
                yield return blockedDismount;
                yield break;
            }

            FloatMenuOption option = new(dismountLabel, () => TryStartDismountJob(actor, moo), MenuOptionPriority.High, null, moo);
            yield return FloatMenuUtility.DecoratePrioritizedTask(option, actor, moo);
            yield break;
        }

        string label = "Mugirl.Mount.Target".Translate(moo.LabelShort);
        if (!comp.CanMount(actor, out string? reasonKey))
        {
            yield return DisabledOption(label, reasonKey, moo);
            yield break;
        }

        if (TryGetBlockedOption(actor, moo, label) is { } blockedMount)
        {
            yield return blockedMount;
            yield break;
        }

        FloatMenuOption mountOption = new(label, () => TryStartMountJob(actor, moo), MenuOptionPriority.High, null, moo);
        yield return FloatMenuUtility.DecoratePrioritizedTask(mountOption, actor, moo);
    }

    // 已被预留或无法到达时给出带原因的禁用选项；可执行时返回 null。
    private static FloatMenuOption? TryGetBlockedOption(Pawn actor, Pawn moo, string label)
    {
        if (!actor.CanReserve(moo))
        {
            return DisabledOption(label, "Mugirl.Mount.ReasonReserved", moo);
        }

        if (!actor.CanReach(moo, PathEndMode.Touch, Danger.Deadly))
        {
            return DisabledOption(label, "Mugirl.Mount.ReasonNoPath", moo);
        }

        return null;
    }

    private static FloatMenuOption DisabledOption(string label, string reasonKey, Pawn moo)
    {
        return new FloatMenuOption("Mugirl.Mount.LabelWithReason".Translate(label, reasonKey.Translate()), null, MenuOptionPriority.High, null, moo);
    }

    private static void TryStartDismountJob(Pawn actor, Pawn carrier)
    {
        CompMount? comp = MountUtility.GetMountComp(carrier);
        if (comp?.HasMountedPawn != true || !actor.CanReserveAndReach(carrier, PathEndMode.Touch, Danger.Deadly))
        {
            return;
        }

        Job job = JobMaker.MakeJob(MountingDefOf.Mugirl_Dismount, carrier);
        actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
    }

    private static void TryStartMountJob(Pawn actor, Pawn carrier)
    {
        CompMount? comp = MountUtility.GetMountComp(carrier);
        if (comp is null
            || !comp.CanMount(actor, out _)
            || !actor.CanReserveAndReach(carrier, PathEndMode.Touch, Danger.Deadly))
        {
            return;
        }

        Job job = JobMaker.MakeJob(MountingDefOf.Mugirl_Mount, carrier);
        actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
    }
}
