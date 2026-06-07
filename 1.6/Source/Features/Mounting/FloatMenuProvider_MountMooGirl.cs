using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class FloatMenuProvider_MountMooGirl : FloatMenuOptionProvider
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
                && MountedPawnUtility.IsMooGirl(target);
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn actor = context.FirstSelectedPawn;
            Pawn moo = clickedPawn;
            if (actor == null || moo == null)
            {
                yield break;
            }

            Comp_MooGirlMount comp = MountedPawnUtility.GetMountComp(moo);
            if (comp == null)
            {
                yield break;
            }

            if (actor == moo)
            {
                if (comp.HasMountedPawn)
                {
                    yield return new FloatMenuOption("MooGirl.Mount.DismountRider".Translate(), () => TryDismountSelf(moo), MenuOptionPriority.High, null, moo);
                }

                yield break;
            }

            if (comp.HasMountedPawn)
            {
                string dismountLabel = "MooGirl.Mount.DismountRider".Translate();
                if (!actor.CanReserve(moo))
                {
                    yield return new FloatMenuOption("MooGirl.Mount.LabelWithReason".Translate(dismountLabel, "MooGirl.Mount.ReasonReserved".Translate()), null, MenuOptionPriority.High, null, moo);
                    yield break;
                }

                if (!actor.CanReach(moo, PathEndMode.Touch, Danger.Deadly))
                {
                    yield return new FloatMenuOption("MooGirl.Mount.LabelWithReason".Translate(dismountLabel, "MooGirl.Mount.ReasonNoPath".Translate()), null, MenuOptionPriority.High, null, moo);
                    yield break;
                }

                FloatMenuOption option = new FloatMenuOption(dismountLabel, delegate
                {
                    TryStartDismountJob(actor, moo);
                }, MenuOptionPriority.High, null, moo);

                yield return FloatMenuUtility.DecoratePrioritizedTask(option, actor, moo);
                yield break;
            }

            string label = "MooGirl.Mount.Target".Translate(moo.LabelShort);
            if (!comp.CanMount(actor, out string reasonKey))
            {
                yield return new FloatMenuOption("MooGirl.Mount.LabelWithReason".Translate(label, reasonKey.Translate()), null, MenuOptionPriority.High, null, moo);
                yield break;
            }

            if (!actor.CanReserve(moo))
            {
                yield return new FloatMenuOption("MooGirl.Mount.LabelWithReason".Translate(label, "MooGirl.Mount.ReasonReserved".Translate()), null, MenuOptionPriority.High, null, moo);
                yield break;
            }

            if (!actor.CanReach(moo, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption("MooGirl.Mount.LabelWithReason".Translate(label, "MooGirl.Mount.ReasonNoPath".Translate()), null, MenuOptionPriority.High, null, moo);
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
            Comp_MooGirlMount comp = MountedPawnUtility.GetMountComp(carrier);
            if (actor == null || carrier == null || comp?.HasMountedPawn != true
                || !actor.CanReserveAndReach(carrier, PathEndMode.Touch, Danger.Deadly))
            {
                return;
            }

            Job job = JobMaker.MakeJob(MooGirl_DefOf.Job_DismountMooGirl, carrier);
            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        private static void TryStartMountJob(Pawn actor, Pawn carrier)
        {
            Comp_MooGirlMount comp = MountedPawnUtility.GetMountComp(carrier);
            if (actor == null || carrier == null || comp == null
                || !comp.CanMount(actor, out _)
                || !actor.CanReserveAndReach(carrier, PathEndMode.Touch, Danger.Deadly))
            {
                return;
            }

            Job job = JobMaker.MakeJob(MooGirl_DefOf.Job_MountMooGirl, carrier);
            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }
    }
}
