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
            if (MooGirlMilkInteractionUtility.HasEnoughForDirectMilkInteraction(comp))
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
            if (!MooGirlMilkInteractionUtility.HasEnoughForDirectMilkInteraction(comp))
                yield break;

            // 榨乳：玩家强制挤奶，任意饱满度大于 5% 即可显示。
            string gatherLabel = "MooGirl.Milk.FloatMenu.Gather".Translate(targetPawn.LabelShortCap);
            if (!pawn.CanReach(targetPawn, PathEndMode.Touch, Danger.Deadly))
            {
                yield return DisabledOption(gatherLabel, "NoPath".Translate().CapitalizeFirst());
                yield break;
            }

            yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                gatherLabel,
                delegate
                {
                    Job job = JobMaker.MakeJob(MooGirl_DefOf.Job_GatherMilk, targetPawn);
                    job.playerForced = true;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }), pawn, targetPawn);

            bool selectedPawnIsChild = !pawn.ageTracker.CurLifeStage.reproductive && pawn.RaceProps.Humanlike;

            // 喝奶：孩子使用“找奶喝”专属互动，避免重复菜单。
            if (!selectedPawnIsChild)
            {
                JobDef drinkDef = MooGirlOptionalDefs.JobDefs.DrinkMilkFromMooGirl;
                if (drinkDef != null)
                {
                    yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                        "MooGirl.Milk.FloatMenu.Drink".Translate(targetPawn.LabelShortCap),
                        delegate
                        {
                            Job job = JobMaker.MakeJob(drinkDef, targetPawn);
                            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                        }), pawn, targetPawn);
                }
            }

            // 找奶喝：仅小孩（不可繁殖阶段）
            if (selectedPawnIsChild)
            {
                JobDef breastfeedDef = MooGirlOptionalDefs.JobDefs.Breastfeed;
                if (breastfeedDef != null)
                {
                    yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                        "MooGirl.Milk.FloatMenu.ChildDrink".Translate(targetPawn.LabelShortCap),
                        delegate
                        {
                            Job job = JobMaker.MakeJob(breastfeedDef, targetPawn);
                            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                        }), pawn, targetPawn);
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

        private static bool IsValidFeedTarget(Pawn target)
        {
            return target != null && target.Spawned && !target.Dead && target.Downed && target.RaceProps?.Humanlike == true;
        }
    }
}
