using Verse.AI;

namespace Mugirl.Features.Restraints;

// 钥匙对自己、其他 Pawn 或尸体开锁的右键菜单，以及选定束具后下达开锁 Job。
internal static class KeyUnlockFloatMenu
{
    // 外层选项：目标无效、未锁定、被预留、不可达或工作类型被禁用时给出禁用项；
    // 点击后列出目标身上的锁定束具，钥匙类型不符的显示为禁用项。
    internal static FloatMenuOption MakeOption(
        CompUsable usable,
        string label,
        Pawn pawn,
        LocalTargetInfo target,
        WorkTypeDef? requiredWork)
    {
        LocalTargetInfo jobTarget = ResolveJobTarget(pawn, target);
        Pawn? targetPawn = ResolveTargetPawn(pawn, jobTarget);
        if (targetPawn?.apparel == null)
        {
            return DisabledOption(label, "Mugirl.InvalidTarget".Translate().ToString());
        }

        if (!HasLockedApparel(targetPawn))
        {
            return DisabledOption(label, "Mugirl.NotWearingLockedApparel".Translate().ToString());
        }

        if (targetPawn != pawn && !pawn.CanReserve(jobTarget))
        {
            return DisabledOption(label, "Mugirl.Reserved".Translate().ToString());
        }

        if (targetPawn != pawn && !pawn.CanReach(jobTarget, PathEndMode.Touch, Danger.Some))
        {
            return DisabledOption(label, "Mugirl.NoPath".Translate().ToString());
        }

        if (requiredWork != null && pawn.WorkTagIsDisabled(requiredWork.workTags))
        {
            return DisabledOption(label, "Mugirl.CannotPrioritizeWorkTypeDisabled".Translate(requiredWork.gerundLabel).ToString());
        }

        return new FloatMenuOption(label, () => OpenApparelSubmenu(usable, pawn, jobTarget), MenuOptionPriority.Default);
    }

    internal static void StartUnlockJob(CompUsable usable, Pawn pawn, LocalTargetInfo target, Apparel apparel)
    {
        LocalTargetInfo jobTarget = ResolveJobTarget(pawn, target);
        Pawn? targetPawn = ResolveTargetPawn(pawn, jobTarget);
        if (targetPawn?.apparel == null
            || !targetPawn.IsWornLockedRestraint(apparel)
            || !apparel.SatisfiesKey(usable.parent))
        {
            return;
        }

        if (!pawn.CanReserveAndReach(usable.parent, PathEndMode.Touch, Danger.Some))
        {
            return;
        }

        if (targetPawn != pawn && !pawn.CanReserveAndReach(jobTarget, PathEndMode.Touch, Danger.Some))
        {
            return;
        }

        CompForbiddable? forbiddable = usable.parent.GetComp<CompForbiddable>();
        if (forbiddable != null)
        {
            forbiddable.Forbidden = false;
        }

        JobDef? useJob = (usable.props as CompProperties_Usable)?.useJob;
        if (useJob == null)
        {
            return;
        }

        Job job = JobMaker.MakeJob(useJob, usable.parent, jobTarget, apparel);
        pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
    }

    private static void OpenApparelSubmenu(CompUsable usable, Pawn pawn, LocalTargetInfo jobTarget)
    {
        Pawn? targetPawn = ResolveTargetPawn(pawn, jobTarget);
        List<Apparel>? wornApparel = targetPawn?.apparel?.WornApparel;
        if (targetPawn == null || wornApparel == null)
        {
            return;
        }

        List<FloatMenuOption> options = new List<FloatMenuOption>();
        for (int i = 0; i < wornApparel.Count; i++)
        {
            Apparel item = wornApparel[i];
            if (!targetPawn.IsWornLockedRestraint(item))
            {
                continue;
            }

            if (item.SatisfiesKey(usable.parent))
            {
                options.Add(new FloatMenuOption(item.Label, () => StartUnlockJob(usable, pawn, jobTarget, item)));
            }
            else
            {
                // 无动作的选项即禁用项；优先级保持 Default，与 1.6 相同。
                options.Add(new FloatMenuOption("Mugirl.WrongKeyType".Translate(item.Label), null, MenuOptionPriority.Default));
            }
        }

        if (options.Count > 0)
        {
            GameUtility.TryAddWindow(new FloatMenu(options));
        }
        else
        {
            Messages.Message("Mugirl.NotWearingLockedApparel".Translate(), MessageTypeDefOf.RejectInput, historical: false);
        }
    }

    private static FloatMenuOption DisabledOption(string label, string reason)
    {
        return new FloatMenuOption(
            "Mugirl.FloatMenu.OptionWithReason".Translate(label, reason),
            null,
            MenuOptionPriority.DisabledOption);
    }

    // 目标无效时视为对自己使用；尸体目标取其中的 Pawn。
    private static Pawn? ResolveTargetPawn(Pawn actor, LocalTargetInfo target)
    {
        if (!target.IsValid)
        {
            return actor;
        }

        return target.Thing switch
        {
            Pawn pawn => pawn,
            Corpse corpse => corpse.InnerPawn,
            _ => null,
        };
    }

    private static LocalTargetInfo ResolveJobTarget(Pawn actor, LocalTargetInfo target)
    {
        return target.IsValid ? target : actor;
    }

    private static bool HasLockedApparel(Pawn pawn)
    {
        List<Apparel>? wornApparel = pawn.apparel?.WornApparel;
        if (wornApparel == null)
        {
            return false;
        }

        for (int i = 0; i < wornApparel.Count; i++)
        {
            if (pawn.IsWornLockedRestraint(wornApparel[i]))
            {
                return true;
            }
        }

        return false;
    }
}
