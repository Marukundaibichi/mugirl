using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    // 为雪牛娘添加右键菜单：榨乳、找奶喝、喝奶、给倒地者喂奶
    public class FloatMenuProvider_MilkMooGirl : FloatMenuOptionProvider
    {
        private const float MinMilkFullnessForMilking = 0.05f;
        private const float MinMilkFullnessForFeeding = 0.05f;

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
            if (comp != null && comp.Active && comp.Fullness > MinMilkFullnessForMilking)
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
            if (comp == null || !comp.Active || comp.Fullness <= MinMilkFullnessForMilking)
                yield break;

            // 榨乳（forced 强制挤奶，任意饱满度 > 5%）
            if (!pawn.CanReach(targetPawn, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption("榨乳 (" + targetPawn.LabelShortCap + "): " + "NoPath".Translate().CapitalizeFirst(), null);
                yield break;
            }

            yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                "榨乳 (" + targetPawn.LabelShortCap + ")",
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
                JobDef drinkDef = DefDatabase<JobDef>.GetNamedSilentFail("Job_DrinkMilkFromMooGirl");
                if (drinkDef != null)
                {
                    yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                        "喝奶 (" + targetPawn.LabelShortCap + ")",
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
                JobDef breastfeedDef = DefDatabase<JobDef>.GetNamedSilentFail("Job_Breastfeed");
                if (breastfeedDef != null)
                {
                    yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                        "找奶喝 (" + targetPawn.LabelShortCap + ")",
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
            string label = "喂奶 (" + target.LabelShortCap + ")";

            JobDef feedJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Job_FeedMilkToDowned");
            if (feedJobDef == null)
                return new FloatMenuOption(label + ": Job_FeedMilkToDowned missing", null);

            if (!milkComp.Active)
                return new FloatMenuOption(label + ": 无法产奶", null);

            if (milkComp.Fullness <= MinMilkFullnessForFeeding)
                return new FloatMenuOption(label + ": 奶量不足", null);

            if (!feeder.CanReach(target, PathEndMode.Touch, Danger.Deadly))
                return new FloatMenuOption(label + ": " + "NoPath".Translate().CapitalizeFirst(), null);

            if (!feeder.CanReserve(target))
                return new FloatMenuOption(label + ": " + "Reserved".Translate().CapitalizeFirst(), null);

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
