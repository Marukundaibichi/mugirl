using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public class FloatMenuProvider_ForceUnlockSlaveApparel : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => true;
        protected override bool RequiresManipulation => true;

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
            Pawn actor = context.FirstSelectedPawn;
            Pawn target = clickedPawn;
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
            if (actor == target && actor.IsHandsBlocked())
            {
                yield return DisabledOption(label, "Mugirl.HandsBlocked".Translate());
                yield break;
            }

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

            yield return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                label,
                delegate
                {
                    List<FloatMenuOption> submenuOptions = new List<FloatMenuOption>();
                    List<Apparel> currentOptions = GetForceUnlockableApparel(target);
                    for (int i = 0; i < currentOptions.Count; i++)
                    {
                        submenuOptions.Add(MakeApparelOption(actor, target, currentOptions[i], currentOptions[i].Label));
                    }

                    if (submenuOptions.Count > 0)
                    {
                        MugirlGameUtility.TryAddWindow(new FloatMenu(submenuOptions));
                    }
                    else
                    {
                        Messages.Message("Mugirl.NotWearingLockedApparel".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                    }
                },
                MenuOptionPriority.High,
                null,
                target), actor, target);
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

            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                label,
                delegate
                {
                    TryStartForceUnlockJob(actor, target, apparel);
                },
                MenuOptionPriority.High,
                null,
                target), actor, target);
        }

        private static void TryStartForceUnlockJob(Pawn actor, Pawn target, Apparel apparel)
        {
            JobDef jobDef = MugirlOptionalDefs.JobDefs.ForceUnlockSlaveApparel;
            if (jobDef == null
                || actor == null
                || target == null
                || apparel == null
                || !ForceUnlockRestraintUtility.IsLockedForceUnlockable(target, apparel)
                || !ForceUnlockRestraintUtility.HasRequiredStrength(actor, apparel)
                || !ForceUnlockRestraintUtility.CanReserveAndReachTarget(actor, target))
            {
                return;
            }

            Job job = JobMaker.MakeJob(jobDef, target, apparel);
            job.playerForced = true;
            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private static List<Apparel> GetForceUnlockableApparel(Pawn target)
        {
            List<Apparel> result = new List<Apparel>();
            if (target?.apparel == null)
            {
                return result;
            }

            List<Apparel> wornApparel = target.apparel.WornApparel;
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
}
