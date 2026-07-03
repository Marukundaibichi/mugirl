using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public class FloatMenuProvider_MountMugirl : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool CanSelfTarget => true;

        public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context)
        {
            return base.SelectedPawnValid(pawn, context) && pawn != null && pawn.Spawned;
        }

        public override bool TargetPawnValid(Pawn target, FloatMenuContext context)
        {
            return base.TargetPawnValid(target, context)
                && target != null
                && target.Spawned
                && !target.Dead
                && MountedPawnUtility.GetMountComp(target) != null
                && MountedPawnUtility.IsMugirl(target);
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn actor = context.FirstSelectedPawn;
            Pawn moo = clickedPawn;
            if (actor == null || moo == null)
            {
                yield break;
            }

            Comp_MugirlMount comp = MountedPawnUtility.GetMountComp(moo);
            if (comp == null)
            {
                yield break;
            }

            if (actor == moo)
            {
                if (comp.HasMountedPawn)
                {
                    yield return new FloatMenuOption("Mugirl.Mount.DismountRider".Translate(), () => TryDismountSelf(moo), MenuOptionPriority.High, null, moo);
                }

                yield break;
            }

            if (comp.HasMountedPawn)
            {
                string dismountLabel = "Mugirl.Mount.DismountRider".Translate();
                if (!actor.CanReserve(moo))
                {
                    yield return new FloatMenuOption("Mugirl.Mount.LabelWithReason".Translate(dismountLabel, "Mugirl.Mount.ReasonReserved".Translate()), null, MenuOptionPriority.High, null, moo);
                    yield break;
                }

                if (!actor.CanReach(moo, PathEndMode.Touch, Danger.Deadly))
                {
                    yield return new FloatMenuOption("Mugirl.Mount.LabelWithReason".Translate(dismountLabel, "Mugirl.Mount.ReasonNoPath".Translate()), null, MenuOptionPriority.High, null, moo);
                    yield break;
                }

                FloatMenuOption option = new FloatMenuOption(dismountLabel, delegate
                {
                    TryStartDismountJob(actor, moo);
                }, MenuOptionPriority.High, null, moo);

                yield return FloatMenuUtility.DecoratePrioritizedTask(option, actor, moo);
                yield break;
            }

            string label = "Mugirl.Mount.Target".Translate(moo.LabelShort);
            if (!comp.CanMount(actor, out string reasonKey))
            {
                yield return new FloatMenuOption("Mugirl.Mount.LabelWithReason".Translate(label, reasonKey.Translate()), null, MenuOptionPriority.High, null, moo);
                yield break;
            }

            if (!actor.CanReserve(moo))
            {
                yield return new FloatMenuOption("Mugirl.Mount.LabelWithReason".Translate(label, "Mugirl.Mount.ReasonReserved".Translate()), null, MenuOptionPriority.High, null, moo);
                yield break;
            }

            if (!actor.CanReach(moo, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption("Mugirl.Mount.LabelWithReason".Translate(label, "Mugirl.Mount.ReasonNoPath".Translate()), null, MenuOptionPriority.High, null, moo);
                yield break;
            }

            FloatMenuOption mountOption = new FloatMenuOption(label, delegate
            {
                TryStartMountJob(actor, moo);
            }, MenuOptionPriority.High, null, moo);

            yield return FloatMenuUtility.DecoratePrioritizedTask(mountOption, actor, moo);
        }

        private static void TryDismountSelf(Pawn carrier)
        {
            MountedPawnUtility.GetMountComp(carrier)?.TryDismount();
        }

        private static void TryStartDismountJob(Pawn actor, Pawn carrier)
        {
            Comp_MugirlMount comp = MountedPawnUtility.GetMountComp(carrier);
            if (actor == null || carrier == null || comp?.HasMountedPawn != true
                || !actor.CanReserveAndReach(carrier, PathEndMode.Touch, Danger.Deadly))
            {
                return;
            }

            Job job = JobMaker.MakeJob(Mugirl_DefOf.Job_DismountMugirl, carrier);
            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private static void TryStartMountJob(Pawn actor, Pawn carrier)
        {
            Comp_MugirlMount comp = MountedPawnUtility.GetMountComp(carrier);
            if (actor == null || carrier == null || comp == null
                || !comp.CanMount(actor, out _)
                || !actor.CanReserveAndReach(carrier, PathEndMode.Touch, Danger.Deadly))
            {
                return;
            }

            Job job = JobMaker.MakeJob(Mugirl_DefOf.Job_MountMugirl, carrier);
            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}
