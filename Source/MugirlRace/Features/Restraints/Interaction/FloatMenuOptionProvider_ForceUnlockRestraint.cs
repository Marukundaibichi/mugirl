using Verse.AI;

namespace Mugirl.Features.Restraints;

// 右键其他 Pawn 时提供“强行开锁”选项（FloatMenuOptionProvider 由原版自动发现）。
public class FloatMenuOptionProvider_ForceUnlockRestraint : FloatMenuOptionProvider
{
    protected override bool Drafted => true;
    protected override bool Undrafted => true;
    protected override bool Multiselect => true;
    protected override bool RequiresManipulation => true;

    // 只对其他 Pawn 生效；施术者自己被排除，因此不存在“自己强行开锁”的选项。
    public override bool TargetPawnValid(Pawn target, FloatMenuContext context)
    {
        return target != null
            && target != context.FirstSelectedPawn
            && target.Spawned
            && !target.Dead
            && ForceUnlockRestraintUtility.HasAnyLockedForceUnlockable(target);
    }

    public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
    {
        Pawn? actor = context.FirstSelectedPawn;
        Pawn? target = clickedPawn;
        if (actor == null || target == null)
        {
            yield break;
        }

        List<Apparel> options = GetForceUnlockableApparel(target);
        if (options.Count == 0)
        {
            yield break;
        }

        string label = "Mugirl.Restraints.ForceUnlock.FloatMenu".Translate(target.LabelShortCap);
        if (!ForceUnlockRestraintUtility.CanReserveAndReachTarget(actor, target))
        {
            yield return DisabledOption(label, "Mugirl.NoPath".Translate());
            yield break;
        }

        if (options.Count == 1)
        {
            yield return MakeApparelOption(actor, target, options[0], label);
            yield break;
        }

        yield return FloatMenuUtility.DecoratePrioritizedTask(
            new FloatMenuOption(label, () => OpenApparelSubmenu(actor, target), MenuOptionPriority.High, null, target),
            actor,
            target);
    }

    private static void OpenApparelSubmenu(Pawn actor, Pawn target)
    {
        List<Apparel> currentOptions = GetForceUnlockableApparel(target);
        List<FloatMenuOption> submenuOptions = new List<FloatMenuOption>(currentOptions.Count);
        for (int i = 0; i < currentOptions.Count; i++)
        {
            submenuOptions.Add(MakeApparelOption(actor, target, currentOptions[i], currentOptions[i].Label));
        }

        if (submenuOptions.Count > 0)
        {
            GameUtility.TryAddWindow(new FloatMenu(submenuOptions));
        }
        else
        {
            Messages.Message("Mugirl.NotWearingLockedApparel".Translate(), MessageTypeDefOf.RejectInput, historical: false);
        }
    }

    private static FloatMenuOption MakeApparelOption(Pawn actor, Pawn target, Apparel apparel, string label)
    {
        if (!ForceUnlockRestraintUtility.HasRequiredStrength(actor, apparel))
        {
            string reason = "Mugirl.Restraints.ForceUnlock.StrengthTooLow".Translate(
                ForceUnlockRestraintUtility.StrengthText(ForceUnlockRestraintUtility.GetStrengthLevel(actor)),
                ForceUnlockRestraintUtility.StrengthText(ForceUnlockRestraintUtility.GetRequiredStrength(apparel)),
                ForceUnlockRestraintUtility.RequirementSummary(apparel));
            return DisabledOption(label, reason);
        }

        return FloatMenuUtility.DecoratePrioritizedTask(
            new FloatMenuOption(label, () => TryStartForceUnlockJob(actor, target, apparel), MenuOptionPriority.High, null, target),
            actor,
            target);
    }

    private static void TryStartForceUnlockJob(Pawn actor, Pawn target, Apparel apparel)
    {
        if (!ForceUnlockRestraintUtility.IsLockedForceUnlockable(target, apparel)
            || !ForceUnlockRestraintUtility.HasRequiredStrength(actor, apparel)
            || !ForceUnlockRestraintUtility.CanReserveAndReachTarget(actor, target))
        {
            return;
        }

        Job job = JobMaker.MakeJob(RestraintsDefOf.Mugirl_ForceUnlockRestraint, target, apparel);
        job.playerForced = true;
        actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
    }

    private static List<Apparel> GetForceUnlockableApparel(Pawn target)
    {
        List<Apparel> result = new List<Apparel>();
        List<Apparel>? wornApparel = target.apparel?.WornApparel;
        if (wornApparel == null)
        {
            return result;
        }

        for (int i = 0; i < wornApparel.Count; i++)
        {
            Apparel apparel = wornApparel[i];
            if (ForceUnlockRestraintUtility.IsLockedForceUnlockable(target, apparel))
            {
                result.Add(apparel);
            }
        }

        return result;
    }

    private static FloatMenuOption DisabledOption(string label, string reason)
    {
        return new FloatMenuOption(
            "Mugirl.FloatMenu.OptionWithReason".Translate(label, reason),
            null,
            MenuOptionPriority.DisabledOption);
    }
}
