using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    // 为雪牛娘添加右键菜单：榨乳、找奶喝、喝奶、给倒地者喂奶
    public class FloatMenuProvider_MilkMooGirl : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => true;
        protected override bool RequiresManipulation => true;

        public override bool TargetPawnValid(Pawn target, FloatMenuContext context)
        {
            if (!base.TargetPawnValid(target, context))
                return false;
            if (target == null || !target.Spawned || target.Dead)
                return false;

            if (IsValidFeedTarget(target) && SelectedPawnHasMilkSource(context))
                return true;

            CompMooMilkable comp = target.TryGetComp<CompMooMilkable>();
            if (MooGirlMilkInteractionUtility.HasAnyMilk(comp))
                return true;

            return false;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            Pawn targetPawn = clickedPawn;

            if (pawn == null || targetPawn == null || targetPawn == pawn)
                yield break;

            CompMooMilkable selectedMilkComp = pawn.TryGetComp<CompMooMilkable>();
            if (selectedMilkComp != null && IsValidFeedTarget(targetPawn))
            {
                yield return GetFeedMilkOption(pawn, targetPawn, selectedMilkComp);
            }

            CompMooMilkable comp = targetPawn.TryGetComp<CompMooMilkable>();
            if (!MooGirlMilkInteractionUtility.HasAnyMilk(comp))
                yield break;

            // 榨乳：玩家强制挤奶，任意饱满度大于 0 即可显示。
            string gatherLabel = "MooGirl.Milk.FloatMenu.Gather".Translate(targetPawn.LabelShortCap);
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
                yield return DisabledOption(gatherLabel, "MooGirl.Milk.FloatMenu.ManagedByDevice".Translate());
            }
            else
            {
                yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                    gatherLabel,
                    delegate
                    {
                        TryStartGatherJob(pawn, targetPawn);
                    }), pawn, targetPawn);
            }

            bool selectedPawnIsChild = MooGirlMilkInteractionUtility.IsChildMilkSeeker(pawn);

            // 喝奶：孩子使用“找奶喝”专属互动，避免重复菜单。
            if (!selectedPawnIsChild)
            {
                JobDef drinkDef = MooGirlOptionalDefs.JobDefs.DrinkMilkFromMooGirl;
                if (drinkDef != null)
                {
                    string drinkLabel = "MooGirl.Milk.FloatMenu.Drink".Translate(targetPawn.LabelShortCap);
                    if (!MooGirlMilkInteractionUtility.CanDrinkMilkNow(pawn, targetPawn))
                    {
                        yield return DisabledOption(drinkLabel, "MooGirl.Milk.FloatMenu.NotEnoughMilk".Translate());
                    }
                    else
                    {
                        yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                            drinkLabel,
                            delegate
                            {
                                TryStartDrinkJob(pawn, targetPawn, drinkDef);
                            }), pawn, targetPawn);
                    }
                }
            }

            // 找奶喝：仅小孩（不可繁殖阶段）
            if (selectedPawnIsChild)
            {
                JobDef breastfeedDef = MooGirlOptionalDefs.JobDefs.Breastfeed;
                if (breastfeedDef != null)
                {
                    string childDrinkLabel = "MooGirl.Milk.FloatMenu.ChildDrink".Translate(targetPawn.LabelShortCap);
                    if (!MooGirlMilkInteractionUtility.CanChildBreastfeedNow(pawn, targetPawn))
                    {
                        yield return DisabledOption(childDrinkLabel, "MooGirl.Milk.FloatMenu.NotEnoughMilk".Translate());
                    }
                    else
                    {
                        yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                            childDrinkLabel,
                            delegate
                            {
                                TryStartBreastfeedJob(pawn, targetPawn, breastfeedDef);
                            }), pawn, targetPawn);
                    }
                }
            }
        }

        private static FloatMenuOption GetFeedMilkOption(Pawn feeder, Pawn target, CompMooMilkable milkComp)
        {
            string label = "MooGirl.Milk.FloatMenu.Feed".Translate(target.LabelShortCap);

            JobDef feedJobDef = MooGirlOptionalDefs.JobDefs.FeedMilkToDowned;
            if (feedJobDef == null)
                return DisabledOption(label, "MooGirl.Milk.FloatMenu.FeedJobMissing".Translate());

            if (!milkComp.Active)
                return DisabledOption(label, "MooGirl.Milk.FloatMenu.NotActive".Translate());

            if (!MooGirlMilkInteractionUtility.HasEnoughForDirectMilkInteraction(milkComp))
                return DisabledOption(label, "MooGirl.Milk.FloatMenu.NotEnoughMilk".Translate());

            if (!feeder.CanReach(target, PathEndMode.Touch, Danger.Deadly))
                return DisabledOption(label, "NoPath".Translate().CapitalizeFirst());

            if (!feeder.CanReserve(target))
                return DisabledOption(label, "Reserved".Translate().CapitalizeFirst());

            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                label,
                delegate
                {
                    if (!MooGirlMilkInteractionUtility.CanFeedDownedPawnNow(feeder, target)
                        || !feeder.CanReserveAndReach(target, PathEndMode.Touch, Danger.Deadly))
                    {
                        return;
                    }

                    Job job = JobMaker.MakeJob(feedJobDef, target);
                    job.playerForced = true;
                    feeder.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                },
                MenuOptionPriority.High,
                null,
                target), feeder, target);
        }

        private static FloatMenuOption DisabledOption(string label, string reason)
        {
            return new FloatMenuOption("MooGirl.Milk.FloatMenu.Disabled".Translate(label, reason), null);
        }

        private static bool SelectedPawnHasMilkSource(FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            return pawn != null && !pawn.Dead && !pawn.Downed && pawn.TryGetComp<CompMooMilkable>() != null;
        }

        private static void TryStartGatherJob(Pawn gatherer, Pawn target)
        {
            CompMooMilkable comp = target?.TryGetComp<CompMooMilkable>();
            if (gatherer == null || target == null || target.Dead || !target.Spawned
                || !MooGirlMilkInteractionUtility.HasAnyMilk(comp)
                || comp.IsManagedByMilkingDevice
                || !gatherer.CanReserveAndReach(target, PathEndMode.Touch, Danger.Deadly))
            {
                return;
            }

            Job job = JobMaker.MakeJob(MooGirl_DefOf.Job_GatherMilk, target);
            job.playerForced = true;
            gatherer.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private static void TryStartDrinkJob(Pawn drinker, Pawn target, JobDef drinkDef)
        {
            if (drinkDef == null
                || !MooGirlMilkInteractionUtility.CanDrinkMilkNow(drinker, target)
                || !drinker.CanReserveAndReach(target, PathEndMode.Touch, Danger.Deadly))
            {
                return;
            }

            Job job = JobMaker.MakeJob(drinkDef, target);
            drinker.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private static void TryStartBreastfeedJob(Pawn child, Pawn target, JobDef breastfeedDef)
        {
            if (breastfeedDef == null
                || !MooGirlMilkInteractionUtility.CanChildBreastfeedNow(child, target)
                || !child.CanReserveAndReach(target, PathEndMode.Touch, Danger.Deadly))
            {
                return;
            }

            Job job = JobMaker.MakeJob(breastfeedDef, target);
            child.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private static bool IsValidFeedTarget(Pawn target)
        {
            return target != null && target.Spawned && !target.Dead && target.Downed && target.RaceProps?.Humanlike == true;
        }
    }
}
