using System.Diagnostics.CodeAnalysis;
using Verse.AI;

namespace Mugirl.Features.Milk;

// 右键雪牛娘或可喂奶目标时的菜单：榨乳、喝奶、小孩找奶喝、雪牛娘给倒地者或站立殖民者喂奶。
// 多选时与 1.6 相同，只由第一个选中的 Pawn 执行（这些互动都只能由一人进行）。
public sealed class FloatMenuOptionProvider_MilkInteraction : FloatMenuOptionProvider
{
    protected override bool Drafted => true;

    protected override bool Undrafted => true;

    protected override bool Multiselect => true;

    protected override bool RequiresManipulation => true;

    public override bool TargetPawnValid(Pawn target, FloatMenuContext context)
    {
        if (!base.TargetPawnValid(target, context) || target == null || !target.Spawned || target.Dead)
        {
            return false;
        }

        if (IsValidFeedTarget(target) && SelectedPawnHasMilkSource(context))
        {
            return true;
        }

        return MilkInteractionUtility.HasAnyMilk(target.TryGetComp<CompMilkProduction>());
    }

    public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
    {
        Pawn? pawn = context.FirstSelectedPawn;
        Pawn? targetPawn = clickedPawn;
        if (pawn == null || targetPawn == null || targetPawn == pawn)
        {
            yield break;
        }

        CompMilkProduction? selectedMilkComp = pawn.TryGetComp<CompMilkProduction>();
        if (selectedMilkComp != null && IsValidFeedTarget(targetPawn))
        {
            yield return GetFeedMilkOption(pawn, targetPawn, selectedMilkComp);
        }

        CompMilkProduction? comp = targetPawn.TryGetComp<CompMilkProduction>();
        if (!MilkInteractionUtility.HasAnyMilk(comp))
        {
            yield break;
        }

        // 榨乳：玩家强制挤奶，任意饱满度大于 0 即可显示。
        string gatherLabel = "Mugirl.Milk.FloatMenu.Gather".Translate(targetPawn.LabelShortCap);
        if (!pawn.CanReach(targetPawn, PathEndMode.Touch, Danger.Deadly))
        {
            yield return DisabledOption(gatherLabel, "NoPath".Translate().CapitalizeFirst());
            yield break;
        }

        if (!pawn.CanReserve(targetPawn))
        {
            yield return DisabledOption(gatherLabel, "Reserved".Translate().CapitalizeFirst());
            yield break;
        }

        if (comp.IsManagedByMilkingDevice)
        {
            yield return DisabledOption(gatherLabel, "Mugirl.Milk.FloatMenu.ManagedByDevice".Translate());
        }
        else
        {
            yield return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption(gatherLabel, () => TryStartGatherJob(pawn, targetPawn)),
                pawn,
                targetPawn);
        }

        // 喝奶：孩子使用“找奶喝”专属互动，避免重复菜单。
        if (MilkInteractionUtility.IsChildMilkSeeker(pawn))
        {
            string childDrinkLabel = "Mugirl.Milk.FloatMenu.ChildDrink".Translate(targetPawn.LabelShortCap);
            yield return MilkInteractionUtility.CanChildBreastfeedNow(pawn, targetPawn)
                ? FloatMenuUtility.DecoratePrioritizedTask(
                    new FloatMenuOption(childDrinkLabel, () => TryStartInteractionJob(pawn, targetPawn, MilkDefOf.Mugirl_ChildDrinkMilk)),
                    pawn,
                    targetPawn)
                : DisabledOption(childDrinkLabel, "Mugirl.Milk.FloatMenu.NotEnoughMilk".Translate());
        }
        else
        {
            string drinkLabel = "Mugirl.Milk.FloatMenu.Drink".Translate(targetPawn.LabelShortCap);
            yield return MilkInteractionUtility.CanDrinkMilkNow(pawn, targetPawn)
                ? FloatMenuUtility.DecoratePrioritizedTask(
                    new FloatMenuOption(drinkLabel, () => TryStartInteractionJob(pawn, targetPawn, MilkDefOf.Mugirl_DrinkMilk)),
                    pawn,
                    targetPawn)
                : DisabledOption(drinkLabel, "Mugirl.Milk.FloatMenu.NotEnoughMilk".Translate());
        }
    }

    private static FloatMenuOption GetFeedMilkOption(Pawn feeder, Pawn target, CompMilkProduction milkComp)
    {
        string label = "Mugirl.Milk.FloatMenu.Feed".Translate(target.LabelShortCap);
        if (!milkComp.Active)
        {
            return DisabledOption(label, "Mugirl.Milk.FloatMenu.NotActive".Translate());
        }

        if (!MilkInteractionUtility.HasEnoughForDirectMilkInteraction(milkComp))
        {
            return DisabledOption(label, "Mugirl.Milk.FloatMenu.NotEnoughMilk".Translate());
        }

        if (!feeder.CanReach(target, PathEndMode.Touch, Danger.Deadly))
        {
            return DisabledOption(label, "NoPath".Translate().CapitalizeFirst());
        }

        if (!feeder.CanReserve(target))
        {
            return DisabledOption(label, "Reserved".Translate().CapitalizeFirst());
        }

        return FloatMenuUtility.DecoratePrioritizedTask(
            new FloatMenuOption(label, () => TryStartFeedJob(feeder, target), MenuOptionPriority.High, null, target),
            feeder,
            target);
    }

    private static FloatMenuOption DisabledOption(string label, string reason)
    {
        return new FloatMenuOption("Mugirl.Milk.FloatMenu.Disabled".Translate(label, reason), null);
    }

    private static bool SelectedPawnHasMilkSource(FloatMenuContext context)
    {
        Pawn? pawn = context.FirstSelectedPawn;
        return pawn != null && !pawn.Dead && !pawn.Downed && pawn.TryGetComp<CompMilkProduction>() != null;
    }

    private static void TryStartGatherJob(Pawn gatherer, Pawn target)
    {
        CompMilkProduction? comp = target.TryGetComp<CompMilkProduction>();
        if (target.Dead || !target.Spawned
            || !MilkInteractionUtility.HasAnyMilk(comp)
            || comp.IsManagedByMilkingDevice
            || !gatherer.CanReserveAndReach(target, PathEndMode.Touch, Danger.Deadly))
        {
            return;
        }

        Job job = JobMaker.MakeJob(MilkDefOf.Mugirl_GatherMilk, target);
        job.playerForced = true;
        gatherer.jobs.TryTakeOrderedJob(job, JobTag.Misc);
    }

    // 喝奶与找奶喝：点击时重新检查条件，菜单打开期间奶量可能已变化。
    private static void TryStartInteractionJob(Pawn actor, Pawn milkSource, JobDef jobDef)
    {
        bool canStart = jobDef == MilkDefOf.Mugirl_ChildDrinkMilk
            ? MilkInteractionUtility.CanChildBreastfeedNow(actor, milkSource)
            : MilkInteractionUtility.CanDrinkMilkNow(actor, milkSource);
        if (!canStart || !actor.CanReserveAndReach(milkSource, PathEndMode.Touch, Danger.Deadly))
        {
            return;
        }

        actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, milkSource), JobTag.Misc);
    }

    private static void TryStartFeedJob(Pawn feeder, Pawn target)
    {
        if (!MilkInteractionUtility.CanFeedPawnNow(feeder, target)
            || !feeder.CanReserveAndReach(target, PathEndMode.Touch, Danger.Deadly))
        {
            return;
        }

        Job job = JobMaker.MakeJob(MilkDefOf.Mugirl_FeedMilk, target);
        job.playerForced = true;
        feeder.jobs.TryTakeOrderedJob(job, JobTag.Misc);
    }

    private static bool IsValidFeedTarget([NotNullWhen(true)] Pawn? target)
    {
        return target != null && target.Spawned && MilkInteractionUtility.IsDirectFeedTarget(target);
    }
}
