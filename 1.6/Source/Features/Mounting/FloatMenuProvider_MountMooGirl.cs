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
            return target != null && target.Spawned && !target.Dead && MountedPawnUtility.GetMountComp(target) != null && MountedPawnUtility.IsMooGirl(target);
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
                    yield return new FloatMenuOption("MooGirl.Mount.DismountRider".Translate(), () => comp.TryDismount(), MenuOptionPriority.High, null, moo);
                }

                yield break;
            }

            if (comp.HasMountedPawn)
            {
                FloatMenuOption option = new FloatMenuOption("MooGirl.Mount.DismountRider".Translate(), delegate
                {
                    Job job = JobMaker.MakeJob(MooGirl_DefOf.Job_DismountMooGirl, moo);
                    actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
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
                Job job = JobMaker.MakeJob(MooGirl_DefOf.Job_MountMooGirl, moo);
                actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }, MenuOptionPriority.High, null, moo);

            yield return FloatMenuUtility.DecoratePrioritizedTask(mountOption, actor, moo);
        }
    }
}
